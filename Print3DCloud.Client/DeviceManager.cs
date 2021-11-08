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
    internal class DeviceManager : IHostedService
    {
        private const string PrinterConfigurationActionName = "printer_configuration";
        private const int ScanDevicesIntervalMs = 1_000;
        private const int RetryConnectionDelayMs = 10_000;

        private readonly ILogger<DeviceManager> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ActionCableClient actionCableClient;

#if DEBUG
        private readonly string dummyPrinterId;
#endif

        private readonly IActionCableSubscription subscription;
        private readonly Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private readonly Dictionary<string, PrinterController> printers = new();
        private readonly Random random = new();
        private readonly CancellationTokenSource cancellationTokenSource = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceManager"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="serviceProvider">Service provider to use when creating <see cref="IPrinter"/> instances.</param>
        /// <param name="actionCableClient">The <see cref="ActionCableClient"/> to use to communicate with the server.</param>
        /// <param name="config">The application's configuration.</param>
        public DeviceManager(ILogger<DeviceManager> logger, IServiceProvider serviceProvider, ActionCableClient actionCableClient, Config config)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.actionCableClient = actionCableClient;

            this.actionCableClient.Disconnected += this.ActionCableClient_Disconnected;

#if DEBUG
            this.dummyPrinterId = $"{config.ClientId}_dummy";
#endif

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
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await this.actionCableClient.ConnectAsync(cancellationToken);
            await this.subscription.SubscribeAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            // TODO: wait for tasks to complete?
            this.cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        private async void Subscription_Subscribed()
        {
            this.logger.LogInformation("Subscribed to client channel");
            await this.PollDevices(this.cancellationTokenSource.Token);
        }

        private void Subscription_Unsubscribed()
        {
            this.logger.LogInformation("Unsubscribed from client channel");
        }

        /// <summary>
        /// Message indicating a device has been configured as a printer.
        /// </summary>
        /// <param name="message">The received message.</param>
        private async void HandlePrinterConfigurationMessage(PrinterConfigurationMessage message)
        {
            if (message.Printer.PrinterDefinition == null || message.Printer.Device == null) return;

            string hardwareIdentifier = message.Printer.Device.HardwareIdentifier;

            if (this.printers.TryGetValue(hardwareIdentifier, out PrinterController? printerController))
            {
                this.logger.LogInformation("Printer '{HardwareIdentifier}' is already connected, applying configuration", hardwareIdentifier);

                if (printerController.Printer is IGCodePrinter gCodePrinter)
                {
                    gCodePrinter.GCodeSettings = message.Printer.PrinterDefinition.GCodeSettings;
                }

                if (printerController.Printer is IUltiGCodePrinter ultiGCodePrinter)
                {
                    ultiGCodePrinter.UltiGCodeSettings = message.Printer.PrinterDefinition.UltiGCodeSettings;
                }

                return;
            }

            this.logger.LogInformation("Attempting to set up printer '{HardwareIdentifier}'", hardwareIdentifier);

            IPrinter printer;

            if (this.discoveredSerialDevices.TryGetValue(hardwareIdentifier, out SerialPortInfo portInfo))
            {
                string driver = message.Printer.PrinterDefinition.Driver;

                switch (driver)
                {
                    case MarlinPrinter.DriverId:
                        printer = ActivatorUtilities.CreateInstance<MarlinPrinter>(
                            this.serviceProvider,
                            new SerialPortFactory(),
                            portInfo.PortName);
                        break;

                    default:
                        this.logger.LogError("Unexpected driver '{Driver}'", driver);
                        return;
                }
            }
#if DEBUG
            else if (hardwareIdentifier == this.dummyPrinterId)
            {
                printer = ActivatorUtilities.CreateInstance<DummyPrinter>(this.serviceProvider);
            }
#endif
            else
            {
                this.logger.LogError("Unknown printer '{HardwareIdentifier}'", hardwareIdentifier);
                return;
            }

            if (printer is IGCodePrinter gCodePrinter2)
            {
                gCodePrinter2.GCodeSettings = message.Printer.PrinterDefinition.GCodeSettings;
            }

            if (printer is IUltiGCodePrinter ultiGCodePrinter2)
            {
                ultiGCodePrinter2.UltiGCodeSettings = message.Printer.PrinterDefinition.UltiGCodeSettings;
            }

            IActionCableSubscription subscription = this.actionCableClient.GetSubscription(new PrinterIdentifier(hardwareIdentifier));
            printerController = ActivatorUtilities.CreateInstance<PrinterController>(this.serviceProvider, printer, subscription);
            this.printers.Add(hardwareIdentifier, printerController);

            try
            {
                await Task.Run(() => printerController.SubscribeAndConnect(this.cancellationTokenSource.Token));

                this.logger.LogInformation("Printer '{HardwareIdentifier}' set up successfully", hardwareIdentifier);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to set up printer '{HardwareIdentifier}'\n{Exception}", hardwareIdentifier, ex);
            }
        }

        private async Task PollDevices(CancellationToken cancellationToken)
        {
            if (this.subscription.State != SubscriptionState.Subscribed) return;

            #if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--dummy-printer"))
            {
                await this.subscription.PerformAsync(new DeviceMessage("dummy0", this.dummyPrinterId, false), CancellationToken.None).ConfigureAwait(false);
            }
            #endif

            while (this.subscription.State == SubscriptionState.Subscribed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await this.ScanSerialDevices(cancellationToken);
                await this.ReportPrinterStates(cancellationToken);

                await Task.Delay(ScanDevicesIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ScanSerialDevices(CancellationToken cancellationToken)
        {
            var portInfos = new List<SerialPortInfo>();

            // check for new devices
            foreach (SerialPortInfo portInfo in SerialPortHelper.GetPorts())
            {
                portInfos.Add(portInfo);

                if (this.discoveredSerialDevices.ContainsKey(portInfo.UniqueId)) continue;

                this.logger.LogInformation("Found new device at {PortName}", portInfo.PortName);

                try
                {
                    await this.subscription.PerformAsync(new DeviceMessage(portInfo.PortName, portInfo.UniqueId, portInfo.IsPortableUniqueId), cancellationToken).ConfigureAwait(false);
                    this.discoveredSerialDevices.Add(portInfo.UniqueId, portInfo);
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Could not send device discovery message\n{Exception}", ex);
                }
            }

            List<string> devicesToRemove = new();

            // check for lost devices
            foreach ((string deviceId, SerialPortInfo portInfo) in this.discoveredSerialDevices)
            {
                if (portInfos.Contains(portInfo)) continue;

                this.logger.LogInformation("Lost device at {PortName}", portInfo.PortName);

                if (this.printers.TryGetValue(deviceId, out PrinterController? printerController))
                {
                    try
                    {
                        await printerController.UnsubscribeAndDisconnect(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError("Could not disconnect printer\n{Exception}", ex);
                    }
                    finally
                    {
                        printerController.Dispose();
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
                            kvp => new PrinterStateWithTemperatures(kvp.Value.State, kvp.Value.Temperatures, kvp.Value.Progress, kvp.Value.TimeRemaining))),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to report printer states\n{Exception}", ex);
            }
        }

        private async void ActionCableClient_Disconnected(bool willReconnect)
        {
            if (willReconnect)
            {
                return;
            }

            this.logger.LogInformation("Cable connection lost, attempting to reconnect in {RetryConnectionDelayMs} ms", RetryConnectionDelayMs);

            CancellationToken cancellationToken = this.cancellationTokenSource.Token;

            await Task.Delay(this.random.Next((int)(RetryConnectionDelayMs * 0.8), (int)(RetryConnectionDelayMs * 1.2)), cancellationToken);
            await this.actionCableClient.ConnectAsync(cancellationToken);
        }
    }
}
