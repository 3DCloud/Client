using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;
using Print3DCloud.Client.Configuration;
using Print3DCloud.Client.Printers;
using Print3DCloud.Client.Printers.Marlin;
using Print3DCloud.Client.Utilities;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Manages discovery of devices and assignment of devices to printers.
    /// </summary>
    internal class DeviceManager
    {
        private const string PrinterConfigurationActionName = "printer_configuration";
        private const int ScanDevicesIntervalMs = 1_000;

        private readonly ILogger<DeviceManager> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ActionCableClient actionCableClient;
        private readonly IHostApplicationLifetime hostApplicationLifetime;

        private readonly string dummyPrinterId;
        private readonly ActionCableSubscription subscription;
        private readonly Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private readonly Dictionary<string, PrinterController> printers = new();

        private CancellationTokenSource? cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceManager"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="serviceProvider">Service provider to use when creating <see cref="IPrinter"/> instances.</param>
        /// <param name="actionCableClient">The <see cref="ActionCableClient"/> to use to communicate with the server.</param>
        /// <param name="config">The application's configuration.</param>
        /// <param name="hostApplicationLifetime">Application lifetime helper.</param>
        public DeviceManager(ILogger<DeviceManager> logger, IServiceProvider serviceProvider, ActionCableClient actionCableClient, Config config, IHostApplicationLifetime hostApplicationLifetime)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.actionCableClient = actionCableClient;
            this.hostApplicationLifetime = hostApplicationLifetime;

            this.dummyPrinterId = $"{config.ClientId}_dummy";
            this.subscription = this.actionCableClient.GetSubscription(new ClientIdentifier(config.ClientId, config.Secret));

            this.subscription.Subscribed += this.Subscription_Subscribed;
            this.subscription.Unsubscribed += this.Subscription_Unsubscribed;

            this.subscription.RegisterCallback<PrinterConfigurationMessage>(PrinterConfigurationActionName, this.HandlePrinterConfigurationMessage);
        }

        /// <summary>
        /// Starts the device manager.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the device manager has started looking for devices.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return this.subscription.SubscribeAsync(cancellationToken);
        }

        private async void Subscription_Subscribed()
        {
            this.logger.LogInformation("Subscribed!");

            this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(this.hostApplicationLifetime.ApplicationStopping);

            try
            {
                await this.PollDevices(this.cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError("Device polling task errored; exiting");
                this.logger.LogError(ex.ToString());
                this.hostApplicationLifetime.StopApplication();
            }

            if (this.subscription.State == SubscriptionState.Subscribed)
            {
                await this.subscription.Unsubscribe(CancellationToken.None);
            }
        }

        private void Subscription_Unsubscribed()
        {
            this.logger.LogInformation("Unsubscribed!");
            this.cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Message indicating a device has been configured as a printer.
        /// </summary>
        /// <param name="message">The received message.</param>
        private async void HandlePrinterConfigurationMessage(PrinterConfigurationMessage message)
        {
            if (message.Printer.PrinterDefinition == null || message.Printer.Device == null) return;

            string hardwareIdentifier = message.Printer.Device.HardwareIdentifier;

            if (this.printers.TryGetValue(hardwareIdentifier, out PrinterController? printerManager))
            {
                this.logger.LogInformation($"Printer '{hardwareIdentifier}' is already connected");
                return;
            }

            IPrinter printer;

            this.logger.LogInformation($"Attempting to set up printer '{hardwareIdentifier}'");

            if (hardwareIdentifier == this.dummyPrinterId)
            {
                printer = new DummyPrinter(this.serviceProvider.GetRequiredService<ILogger<DummyPrinter>>());
            }
            else if (this.discoveredSerialDevices.TryGetValue(hardwareIdentifier, out SerialPortInfo portInfo))
            {
                string driver = message.Printer.PrinterDefinition.Driver;

                switch (driver)
                {
                    case MarlinPrinter.DriverId:
                        printer = new MarlinPrinter(new SerialPortFactory(), this.serviceProvider.GetRequiredService<ILogger<MarlinPrinter>>(), portInfo.PortName);
                        break;

                    default:
                        this.logger.LogError($"Unexpected driver '{driver}'");
                        return;
                }
            }
            else
            {
                this.logger.LogError($"Unknown printer '{hardwareIdentifier}'");
                return;
            }

            ActionCableSubscription subscription = this.actionCableClient.GetSubscription(new PrinterIdentifier(hardwareIdentifier));

            printerManager = new PrinterController(printer, subscription, this.serviceProvider.GetRequiredService<ILogger<PrinterController>>());
            this.printers.Add(hardwareIdentifier, printerManager);

            try
            {
                await Task.Run(() => printerManager.SubscribeAndConnect(CancellationToken.None));

                this.logger.LogInformation($"Printer '{hardwareIdentifier}' set up successfully");
            }
            catch (Exception ex)
            {
                this.logger.LogError($"Failed to set up printer '{hardwareIdentifier}': {ex.Message}");
                this.logger.LogError(ex.StackTrace);
            }
        }

        private async Task PollDevices(CancellationToken cancellationToken)
        {
            if (this.subscription?.State != SubscriptionState.Subscribed) return;

            if (Environment.GetCommandLineArgs().Contains("--dummy-printer"))
            {
                await this.subscription.PerformAsync(new DeviceMessage("dummy0", this.dummyPrinterId, false), CancellationToken.None).ConfigureAwait(false);
            }

            while (this.subscription?.State == SubscriptionState.Subscribed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await this.ScanDevices(cancellationToken);
                await this.ReportPrinterStates(cancellationToken);

                await Task.Delay(ScanDevicesIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ScanDevices(CancellationToken cancellationToken)
        {
            var portInfos = new List<SerialPortInfo>();

            // check for new devices
            foreach (SerialPortInfo portInfo in SerialPortHelper.GetPorts())
            {
                portInfos.Add(portInfo);

                if (this.discoveredSerialDevices.ContainsKey(portInfo.UniqueId)) continue;

                this.logger.LogInformation("Found new device at " + portInfo.PortName);

                try
                {
                    await this.subscription.PerformAsync(new DeviceMessage(portInfo.PortName, portInfo.UniqueId, portInfo.IsPortableUniqueId), cancellationToken).ConfigureAwait(false);
                    this.discoveredSerialDevices.Add(portInfo.UniqueId, portInfo);
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Could not send device discovery message");
                    this.logger.LogError(ex.ToString());
                }
            }

            var devicesToRemove = new List<string>();

            // check for lost devices
            foreach ((string deviceId, SerialPortInfo portInfo) in this.discoveredSerialDevices)
            {
                if (portInfos.Contains(portInfo)) continue;

                this.logger.LogInformation("Lost device at " + portInfo.PortName);

                if (this.printers.TryGetValue(deviceId, out PrinterController? printerMessageForwarder))
                {
                    try
                    {
                        printerMessageForwarder.Dispose();
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError("Could not disconnect printer");
                        this.logger.LogError(ex.ToString());
                    }

                    this.printers.Remove(deviceId);
                }

                devicesToRemove.Add(deviceId);
            }

            foreach (string hardwareIdentifier in devicesToRemove)
            {
                this.discoveredSerialDevices.Remove(hardwareIdentifier);
            }
        }

        private async Task ReportPrinterStates(CancellationToken cancellationToken)
        {
            if (this.actionCableClient.State != ClientState.Connected || this.subscription.State != SubscriptionState.Subscribed)
            {
                return;
            }

            try
            {
                await this.subscription.PerformAsync(
                    new PrinterStatesMessage(
                        this.printers.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new PrinterStateWithTemperatures(kvp.Value.Printer.State, kvp.Value.Printer.Temperatures))),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to report printer states");
                this.logger.LogError(ex.ToString());
            }
        }
    }
}
