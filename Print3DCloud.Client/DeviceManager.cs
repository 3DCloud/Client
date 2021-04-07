using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    internal class DeviceManager : IMessageReceiver
    {
        private readonly ILogger<DeviceManager> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly ActionCableClient actionCableClient;
        private readonly Config config;
        private readonly IHostApplicationLifetime hostApplicationLifetime;

        private readonly Dictionary<string, SerialPortInfo> discoveredSerialDevices = new();
        private readonly Dictionary<string, IPrinter> printers = new();

        private ActionCableSubscription? subscription;
        private Task? pollDevicesTask;
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
        }

        /// <summary>
        /// Starts the device manager.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the device manager has started looking for devices.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return this.actionCableClient.Subscribe(new ClientIdentifier(this.config.ClientId, this.config.Secret), this, cancellationToken);
        }

        /// <inheritdoc/>
        public void Subscribed(ActionCableSubscription subscription)
        {
            this.logger.LogInformation("Subscribed!");
            this.subscription = subscription;

            if (this.pollDevicesTask != null)
            {
                this.cancellationTokenSource?.Cancel();

                this.pollDevicesTask.ContinueWith(t =>
                {
                    this.StartPollDevices();
                });
            }
            else
            {
                this.StartPollDevices();
            }
        }

        /// <inheritdoc/>
        public void Rejected()
        {
            this.logger.LogInformation("Rejected!");
        }

        /// <inheritdoc/>
        public void Unsubscribed()
        {
            this.logger.LogInformation("Unsubscribed!");
        }

        private void StartPollDevices()
        {
            this.cancellationTokenSource = new CancellationTokenSource();
            this.pollDevicesTask = this.PollDevices(this.cancellationTokenSource.Token).ContinueWith(this.HandleTaskCompleted);
        }

        /// <summary>
        /// Message indicating a device has been configured as a printer.
        /// </summary>
        /// <param name="message">The received message.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been processed.</returns>
        [ActionMethod]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private member", Justification = "Called via ActionCableClient")]
        private async Task PrinterConfiguration(PrinterConfigurationMessage message)
        {
            if (message.Printer.PrinterDefinition == null) return;

            string? hardwareIdentifier = message.Printer.Device?.HardwareIdentifier;

            if (string.IsNullOrEmpty(hardwareIdentifier))
            {
                throw new InvalidOperationException("Hardware identifier is empty");
            }

            if (!this.printers.TryGetValue(hardwareIdentifier, out IPrinter? printer))
            {
                this.logger.LogInformation($"Connecting to printer '{hardwareIdentifier}'");

                if (this.discoveredSerialDevices.TryGetValue(hardwareIdentifier, out SerialPortInfo portInfo))
                {
                    string driver = message.Printer.PrinterDefinition.Driver;

                    switch (driver)
                    {
                        case "marlin":
                            printer = new MarlinPrinter(this.serviceProvider.GetRequiredService<ILogger<MarlinPrinter>>(), portInfo.PortName);
                            break;

                        default:
                            this.logger.LogError($"Unexpected driver '{driver}'");
                            return;
                    }
                }
                else if (hardwareIdentifier.EndsWith("_dummy0"))
                {
                    printer = new DummyPrinter(this.serviceProvider.GetRequiredService<ILogger<DummyPrinter>>());
                }
                else
                {
                    this.logger.LogError("Unknown printer requested");
                    return;
                }

                this.printers.Add(hardwareIdentifier, printer);
            }
            else
            {
                this.logger.LogInformation($"Printer '{hardwareIdentifier}' is already connected");
            }

            var printerMessageForwarder = new PrinterMessageForwarder(this.serviceProvider.GetRequiredService<ILogger<PrinterMessageForwarder>>(), printer);
            await this.actionCableClient.Subscribe(new PrinterIdentifier(hardwareIdentifier), printerMessageForwarder, CancellationToken.None);
        }

        private async Task PollDevices(CancellationToken cancellationToken)
        {
            if (this.subscription?.State != SubscriptionState.Subscribed) return;

            // cancel if requested or if app is shutting down
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.hostApplicationLifetime.ApplicationStopping).Token;

            if (Environment.GetCommandLineArgs().Contains("--dummy-printer"))
            {
                await this.subscription.Perform(new DeviceMessage("dummy0", this.config.ClientId + "_dummy0", false), CancellationToken.None).ConfigureAwait(false);
            }

            var portInfos = new List<SerialPortInfo>();

            while (this.subscription?.State == SubscriptionState.Subscribed)
            {
                portInfos.Clear();

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

                // check for lost devices
                foreach ((string deviceId, SerialPortInfo portInfo) in this.discoveredSerialDevices)
                {
                    if (portInfos.Contains(portInfo)) continue;

                    this.logger.LogInformation("Lost device at " + portInfo.PortName);

                    if (this.printers.TryGetValue(deviceId, out IPrinter? printer))
                    {
                        try
                        {
                            printer.Dispose();
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

                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
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
