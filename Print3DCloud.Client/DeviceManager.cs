using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    internal class DeviceManager : IMessageReceiver
    {
        private ILogger<DeviceManager> logger;
        private IServiceProvider serviceProvider;
        private ActionCableClient actionCableClient;
        private Config config;
        private IHostApplicationLifetime hostApplicationLifetime;

        private Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private Dictionary<string, IPrinter> printers = new();

        private ActionCableSubscription? subscription;

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
        }

        /// <summary>
        /// Starts the device manager.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once the device manager has started looking for devices.</returns>
        public Task StartAsync()
        {
            return this.actionCableClient.Subscribe(new ClientIdentifier(this.config.Guid, this.config.Key), this, CancellationToken.None);
        }

        /// <inheritdoc/>
        public void Subscribed(ActionCableSubscription subscription)
        {
            this.logger.LogInformation("Subscribed!");
            this.subscription = subscription;
            this.PollDevices().ContinueWith(this.HandleTaskCompleted);
        }

        /// <inheritdoc/>
        public void Rejected(ActionCableSubscription subscription)
        {
            this.logger.LogInformation("Rejected!");
        }

        /// <inheritdoc/>
        public void Unsubscribed()
        {
            this.logger.LogInformation("Unsubscribed!");
        }

        /// <summary>
        /// boink.
        /// </summary>
        /// <param name="message">The received message.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been processed.</returns>
        [ActionMethod]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private member", Justification = "Called via ActionCableClient")]
        private async Task PrinterConfiguration(PrinterConfigurationMessage message)
        {
            if (message.Printer.PrinterDefinition == null) return;

            string deviceId = message.Printer.DeviceId;
            string driver = message.Printer.PrinterDefinition.Driver;

            if (!this.discoveredSerialDevices.TryGetValue(deviceId, out SerialPortInfo portInfo)) return;

            IGcodePrinter printer;

            switch (driver)
            {
                case "marlin":
                    printer = new MarlinPrinter(this.serviceProvider.GetRequiredService<ILogger<MarlinPrinter>>(), portInfo.PortName);
                    break;

                default:
                    this.logger.LogError($"Unexpected driver '{driver}'");
                    return;
            }

            this.printers.Add(deviceId, printer);

            await printer.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            if (message.Printer.PrinterDefinition.StartGcode != null)
            {
                await printer.SendCommandBlockAsync(message.Printer.PrinterDefinition.StartGcode, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task PollDevices()
        {
            var portInfos = new List<SerialPortInfo>();

            while (this.subscription?.State == SubscriptionState.Subscribed)
            {
                portInfos.Clear();

                // kill if app is shutting down
                this.hostApplicationLifetime.ApplicationStopping.ThrowIfCancellationRequested();

                // check for new devices
                foreach (SerialPortInfo portInfo in SerialPortHelper.GetPorts())
                {
                    portInfos.Add(portInfo);

                    if (this.discoveredSerialDevices.ContainsKey(portInfo.UniqueId)) continue;

                    this.logger.LogInformation("Found new device at " + portInfo.PortName);

                    await this.subscription.Perform(new DeviceMessage(portInfo.PortName, portInfo.UniqueId, portInfo.IsPortableUniqueId), CancellationToken.None).ConfigureAwait(false);

                    this.discoveredSerialDevices.Add(portInfo.UniqueId, portInfo);
                }

                // check for lost devices
                foreach ((string deviceId, SerialPortInfo portInfo) in this.discoveredSerialDevices)
                {
                    if (portInfos.Contains(portInfo)) continue;

                    this.logger.LogInformation("Lost device at " + portInfo.PortName);

                    if (this.printers.TryGetValue(deviceId, out IPrinter? printer))
                    {
                        try
                        {
                            await printer.DisconnectAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogError("Could not disconnect printer");
                            this.logger.LogError(ex.ToString());
                        }

                        this.printers.Remove(deviceId);
                    }

                    this.discoveredSerialDevices.Remove(deviceId);
                }

                await Task.Delay(5000).ConfigureAwait(false);
            }

            this.logger.LogInformation("Device polling loop exited");
        }

        private void HandleTaskCompleted(Task task)
        {
            if (task.IsFaulted)
            {
                this.logger.LogError("Task errored; exiting");
                this.logger.LogError(task.Exception!.ToString());
                this.hostApplicationLifetime.StopApplication();
            }
        }
    }
}
