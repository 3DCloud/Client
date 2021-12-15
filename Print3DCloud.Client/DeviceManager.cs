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
        private const string DummyPrinterDeviceName = "dummy";
        private const string DummyPrinterDevicePath = "dummy";
        private const int ScanDevicesIntervalMs = 1_000;
        private const int RetryConnectionDelayMs = 10_000;
        private const int PrinterConnectTimeOutMs = 15_000;

        private readonly ILogger<DeviceManager> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ActionCableClient actionCableClient;

        private readonly IActionCableSubscription subscription;
        private readonly Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private readonly Dictionary<string, Printer> printers = new();
        private readonly Random random = new();
        private readonly CancellationTokenSource cancellationTokenSource = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceManager"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="serviceProvider">Service provider to use when creating <see cref="Printer"/> instances.</param>
        /// <param name="actionCableClient">The <see cref="ActionCableClient"/> to use to communicate with the server.</param>
        public DeviceManager(ILogger<DeviceManager> logger, IServiceProvider serviceProvider, ActionCableClient actionCableClient)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.actionCableClient = actionCableClient;

            this.actionCableClient.Disconnected += this.ActionCableClient_Disconnected;

            this.subscription = this.actionCableClient.GetSubscription(new Identifier("ClientChannel"));

            this.subscription.Subscribed += this.Subscription_Subscribed;
            this.subscription.Unsubscribed += this.Subscription_Unsubscribed;

            this.subscription.RegisterAcknowledgeableCallback<PrinterConfigurationMessage>(PrinterConfigurationActionName, this.HandlePrinterConfigurationMessage);
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
        /// <param name="ack">A function to call to acknowledge the message was received.</param>
        private async void HandlePrinterConfigurationMessage(PrinterConfigurationMessage message, AcknowledgeCallback ack)
        {
            ack();

            if (message.Printer.PrinterDefinition == null || message.Printer.Device == null) return;

            string devicePath = message.Printer.Device.Path;

            if (this.printers.TryGetValue(devicePath, out Printer? printer))
            {
                this.logger.LogInformation("Printer '{DevicePath}' is already connected, applying configuration", devicePath);

                if (printer is IGCodePrinter gCodePrinter)
                {
                    gCodePrinter.GCodeSettings = message.Printer.PrinterDefinition.GCodeSettings;
                }

                if (printer is IUltiGCodePrinter ultiGCodePrinter)
                {
                    ultiGCodePrinter.UltiGCodeSettings = message.Printer.UltiGCodeSettings;
                }

                return;
            }

            this.logger.LogInformation("Attempting to set up printer '{DevicePath}'", devicePath);

            // lazy-load subscription in case we don't set up the printer
            IActionCableSubscription GetSubscription()
            {
                return this.actionCableClient.GetSubscription(new PrinterIdentifier(devicePath));
            }

            if (this.discoveredSerialDevices.TryGetValue(devicePath, out SerialPortInfo? portInfo))
            {
                string driver = message.Printer.PrinterDefinition.Driver;

                switch (driver)
                {
                    case MarlinPrinter.DriverId:
                        printer = ActivatorUtilities.CreateInstance<MarlinPrinter>(
                            this.serviceProvider,
                            GetSubscription(),
                            new SerialPortFactory(),
                            portInfo.PortName);
                        break;

                    default:
                        this.logger.LogError("Unexpected driver '{Driver}'", driver);
                        return;
                }
            }
#if DEBUG
            else if (devicePath == DummyPrinterDevicePath)
            {
                printer = ActivatorUtilities.CreateInstance<DummyPrinter>(this.serviceProvider, GetSubscription());
            }
#endif
            else
            {
                this.logger.LogError("Unknown printer '{DevicePath}'", devicePath);
                return;
            }

            if (printer is IGCodePrinter gCodePrinter2)
            {
                gCodePrinter2.GCodeSettings = message.Printer.PrinterDefinition.GCodeSettings;
            }

            if (printer is IUltiGCodePrinter ultiGCodePrinter2)
            {
                ultiGCodePrinter2.UltiGCodeSettings = message.Printer.UltiGCodeSettings;
            }

            this.printers.Add(devicePath, printer);

            try
            {
                CancellationToken cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(
                    this.cancellationTokenSource.Token,
                    new CancellationTokenSource(PrinterConnectTimeOutMs).Token).Token;

                await Task.Run(() => printer.SubscribeAndConnect(cancellationToken), cancellationToken);

                this.logger.LogInformation("Printer '{DevicePath}' set up successfully", devicePath);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to set up printer '{DevicePath}'\n{Exception}", devicePath, ex);
            }
        }

        private async Task PollDevices(CancellationToken cancellationToken)
        {
            if (this.subscription.State != SubscriptionState.Subscribed) return;

            #if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--dummy-printer"))
            {
                await this.subscription.PerformAsync(new DeviceMessage(DummyPrinterDeviceName, DummyPrinterDevicePath, null), CancellationToken.None).ConfigureAwait(false);
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

                if (this.discoveredSerialDevices.ContainsKey(portInfo.DevicePath)) continue;

                this.logger.LogInformation("Found new device at {PortName} ({DevicePath})", portInfo.PortName, portInfo.DevicePath);

                try
                {
                    await this.subscription.PerformAsync(new DeviceMessage(portInfo.PortName, portInfo.DevicePath, portInfo.SerialNumber), cancellationToken).ConfigureAwait(false);
                    this.discoveredSerialDevices.Add(portInfo.DevicePath, portInfo);
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

                if (this.printers.TryGetValue(deviceId, out Printer? printer))
                {
                    try
                    {
                        await printer.UnsubscribeAndDisconnect(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogError("Could not disconnect printer\n{Exception}", ex);
                    }
                    finally
                    {
                        printer.Dispose();
                    }

                    this.printers.Remove(deviceId);
                }

                devicesToRemove.Add(deviceId);
            }

            foreach (string devicePath in devicesToRemove)
            {
                this.discoveredSerialDevices.Remove(devicePath);
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

        private async void ActionCableClient_Disconnected(bool willReconnect, string? reason)
        {
            this.logger.LogInformation("Cable connection lost");

            // force re-send of devices once reconnected
            this.discoveredSerialDevices.Clear();

            if (willReconnect)
            {
                return;
            }

            CancellationToken cancellationToken = this.cancellationTokenSource.Token;

            if (reason != "server_restart")
            {
                this.logger.LogInformation("Waiting {Delay} ms before reconnecting", RetryConnectionDelayMs);
                await Task.Delay((int)(RetryConnectionDelayMs * (0.8 + this.random.NextDouble() * 0.4)), cancellationToken);
            }

            await this.actionCableClient.ConnectAsync(cancellationToken);
        }
    }
}
