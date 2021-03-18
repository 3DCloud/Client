using System;
using System.Text.Json;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.Configuration;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Receives messages from a client channel subscription.
    /// </summary>
    internal class ClientMessageReceiver : MessageReceiver
    {
        private ILogger<ClientMessageReceiver> logger;
        private Config config;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientMessageReceiver"/> class.
        /// </summary>
        /// <param name="config">The global client configuration.</param>
        public ClientMessageReceiver(Config config)
        {
            this.logger = Logging.LoggerFactory.CreateLogger<ClientMessageReceiver>();
            this.config = config;
        }

        /// <inheritdoc/>
        public override void Subscribed(ActionCableSubscription subscription)
        {
            this.logger.LogDebug("Subscribed");
        }

        /// <inheritdoc/>
        public override void Rejected(ActionCableSubscription subscription)
        {
            this.logger.LogDebug("Rejected");
        }

        /// <inheritdoc/>
        public override void Unsubscribed()
        {
            this.logger.LogDebug("Unsubscribed");
        }

        /// <summary>
        /// Triggered when the server sends an authentication key for this client.
        /// </summary>
        /// <param name="jsonElement">Message data.</param>
        public void AuthKey(JsonElement jsonElement)
        {
            this.config.Key = jsonElement.GetProperty("key").GetString();
        }
    }
}
