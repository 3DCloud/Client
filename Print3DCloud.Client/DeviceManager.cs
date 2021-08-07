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
using Print3DCloud.Client.Utilities;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Manages discovery of devices and assignment of devices to printers.
    /// </summary>
    internal class DeviceManager
    {
        private const string PrinterConfigurationActionName = "printer_configuration";

        private readonly ILogger<DeviceManager> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ActionCableClient actionCableClient;
        private readonly Config config;
        private readonly IHostApplicationLifetime hostApplicationLifetime;

        private readonly string dummyPrinterId;
        private readonly ActionCableSubscription subscription;
        private readonly Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private readonly Dictionary<string, PrinterMessageForwarder> printerMessageForwarders = new();

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
            this.config = config;
            this.hostApplicationLifetime = hostApplicationLifetime;

            this.dummyPrinterId = $"{config.ClientId}_dummy0";
            this.subscription = this.actionCableClient.CreateSubscription(new ClientIdentifier(this.config.ClientId, this.config.Secret));

            this.subscription.Connected += this.Subscription_Connected;
            this.subscription.Disconnected += this.Subscription_Disconnected;

            this.subscription.RegisterCallback<PrinterConfigurationMessage>(PrinterConfigurationActionName, this.PrinterConfiguration);
        }

        /// <summary>
        /// Starts the device manager.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the device manager has started looking for devices.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return this.subscription.Subscribe(cancellationToken);
        }

        private async void Subscription_Connected()
        {
            this.logger.LogInformation("Subscribed!");

            this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(this.hostApplicationLifetime.ApplicationStopping);

            try
            {
                await this.PollDevices(this.cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Device polling task errored; exiting");
                this.logger.LogError(ex.ToString());
                this.hostApplicationLifetime.StopApplication();
            }

            await this.subscription.Unsubscribe(CancellationToken.None);
        }

        private void Subscription_Disconnected()
        {
            this.logger.LogInformation("Unsubscribed!");
            this.cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Message indicating a device has been configured as a printer.
        /// </summary>
        /// <param name="message">The received message.</param>
        private async void PrinterConfiguration(PrinterConfigurationMessage message)
        {
            if (message.Printer.PrinterDefinition == null || message.Printer.Device == null) return;

            string hardwareIdentifier = message.Printer.Device.HardwareIdentifier;

            if (this.printerMessageForwarders.TryGetValue(hardwareIdentifier, out PrinterMessageForwarder? printerMessageForwarder))
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
                        printer = new MarlinPrinter(this.serviceProvider.GetRequiredService<ILogger<MarlinPrinter>>(), portInfo.PortName);
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

            ActionCableSubscription subscription = this.actionCableClient.CreateSubscription(new PrinterIdentifier(hardwareIdentifier));

            printerMessageForwarder = new PrinterMessageForwarder(printer, subscription);
            this.printerMessageForwarders.Add(hardwareIdentifier, printerMessageForwarder);
            await Task.Run(() => printerMessageForwarder.SubscribeAndConnect(CancellationToken.None)).ContinueWith(this.OnPrinterTaskCompleted);

            this.logger.LogInformation($"Printer '{hardwareIdentifier}' set up successfully");
        }

        private void OnPrinterTaskCompleted(Task task)
        {
            if (task.IsFaulted)
            {
                this.logger.LogError("Printer task errored: " + task.Exception!.Message);
                this.logger.LogError(task.Exception!.ToString());
            }
            else if (task.IsCanceled)
            {
                this.logger.LogWarning("Printer task canceled");
            }
            else
            {
                this.logger.LogTrace("Printer task completed successfully");
            }
        }

        private async Task PollDevices(CancellationToken cancellationToken)
        {
            if (this.subscription?.State != SubscriptionState.Subscribed) return;

            if (Environment.GetCommandLineArgs().Contains("--dummy-printer"))
            {
                await this.subscription.Perform(new DeviceMessage("dummy0", this.dummyPrinterId, false), CancellationToken.None).ConfigureAwait(false);
            }

            while (this.subscription?.State == SubscriptionState.Subscribed)
            {
                var portInfos = new List<SerialPortInfo>();

                cancellationToken.ThrowIfCancellationRequested();

                // check for new devices
                foreach (SerialPortInfo portInfo in SerialPortHelper.GetPorts())
                {
                    portInfos.Add(portInfo);

                    if (this.discoveredSerialDevices.ContainsKey(portInfo.UniqueId)) continue;

                    this.logger.LogInformation("Found new device at " + portInfo.PortName);

                    try
                    {
                        await this.subscription.Perform(new DeviceMessage(portInfo.PortName, portInfo.UniqueId, portInfo.IsPortableUniqueId), cancellationToken).ConfigureAwait(false);
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

                    if (this.printerMessageForwarders.TryGetValue(deviceId, out PrinterMessageForwarder? printerMessageForwarder))
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

                        this.printerMessageForwarders.Remove(deviceId);
                    }

                    devicesToRemove.Add(deviceId);
                }

                foreach (string hardwareIdentifier in devicesToRemove)
                {
                    this.discoveredSerialDevices.Remove(hardwareIdentifier);
                }

                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            }

            this.logger.LogInformation("Device polling loop exited");
        }
    }
}
