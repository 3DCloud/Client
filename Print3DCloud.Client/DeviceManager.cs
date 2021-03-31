using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;
using Print3DCloud.Client.Configuration;
using Print3DCloud.Client.Utilities;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Manages discovery of devices and assignment of devices to printers.
    /// </summary>
    internal class DeviceManager : IMessageReceiver
    {
        private ILogger<DeviceManager> logger;
        private ActionCableClient actionCableClient;
        private Config config;
        private IHostApplicationLifetime hostApplicationLifetime;

        private List<SerialPortInfo> discoveredDevices;

        private ActionCableSubscription? subscription;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceManager"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="actionCableClient">The <see cref="ActionCableClient"/> to use to communicate with the server.</param>
        /// <param name="config">The application's configuration.</param>
        /// <param name="hostApplicationLifetime">Application lifetime helper.</param>
        public DeviceManager(ILogger<DeviceManager> logger, ActionCableClient actionCableClient, Config config, IHostApplicationLifetime hostApplicationLifetime)
        {
            this.logger = logger;
            this.actionCableClient = actionCableClient;
            this.config = config;
            this.hostApplicationLifetime = hostApplicationLifetime;

            this.discoveredDevices = new List<SerialPortInfo>();
        }

        /// <summary>
        /// Starts the device manager.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once the device manager has started looking for devices.</returns>
        public Task StartAsync()
        {
            return this.actionCableClient.Subscribe(new ClientIdentifier(this.config.Guid, this.config.Key), this, CancellationToken.None).ContinueWith(this.HandleTaskCompleted);
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

                    if (this.discoveredDevices.Contains(portInfo)) continue;

                    this.logger.LogInformation("Found new device at " + portInfo.PortName);

                    await this.subscription.Perform(new DeviceMessage(portInfo.PortName, portInfo.UniqueId, portInfo.IsPortableUniqueId), CancellationToken.None);

                    this.discoveredDevices.Add(portInfo);
                }

                // check for lost devices
                for (int i = 0; i < this.discoveredDevices.Count;)
                {
                    SerialPortInfo portInfo = this.discoveredDevices[i];

                    if (!portInfos.Contains(portInfo))
                    {
                        this.logger.LogInformation("Lost device at " + portInfo.PortName);
                        this.discoveredDevices.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }

                await Task.Delay(5000);
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
