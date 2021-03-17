using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;

namespace ActionCableSharp
{
    /// <summary>
    /// Encapsulates a subscription created when subscribing to a channel through an <see cref="ActionCableClient"/> instance.
    /// </summary>
    public class ActionCableSubscription
    {
        private readonly ActionCableClient client;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableSubscription"/> class.
        /// </summary>
        /// <param name="client">The <see cref="ActionCableClient"/> instance to which this subscription belongs.</param>
        /// <param name="identifier">The <see cref="Identifier"/> used to identifiy this subscription when communicating with the server.</param>
        internal ActionCableSubscription(ActionCableClient client, Identifier identifier)
        {
            this.Identifier = identifier;
            this.State = SubscriptionState.Pending;

            this.client = client;
        }

        /// <summary>
        /// Triggered when a message for this subscription is received.
        /// </summary>
        public event Action<ActionCableMessage>? MessageReceived;

        /// <summary>
        /// Gets the <see cref="Identifier"/> used to identifiy this subscription when communicating with the server.
        /// </summary>
        public Identifier Identifier { get; }

        /// <summary>
        /// Gets the subscription's current state.
        /// </summary>
        public SubscriptionState State { get; private set; }

        /// <summary>
        /// Perform an action on the server.
        /// </summary>
        /// <param name="data"><see cref="ActionMessage"/> that contains the method name and optional data.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent.</returns>
        public Task Perform(ActionMessage data, CancellationToken cancellationToken)
        {
            return this.client.SendMessageAsync("message", this.Identifier, cancellationToken, data);
        }

        /// <summary>
        /// Unsubscribe from this subscription on the server.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the unsubscription request has been sent to the server.</returns>
        public async Task Unsubscribe(CancellationToken cancellationToken)
        {
            await this.client.Unsubscribe(this, cancellationToken);

            this.State = SubscriptionState.Unsubscribed;
        }

        /// <summary>
        /// Handle a message received by the client.
        /// </summary>
        /// <param name="message">Received message.</param>
        internal void HandleMessage(ActionCableIncomingMessage message)
        {
            Identifier? identifier = (Identifier?)JsonSerializer.Deserialize(message.Identifier, this.Identifier.GetType(), this.client.JsonSerializerOptions);

            if (!this.Identifier.Equals(identifier))
            {
                return;
            }

            switch (message.Type)
            {
                case MessageType.Confirmation:
                    this.State = SubscriptionState.Subscribed;
                    break;

                case MessageType.Rejection:
                    this.State = SubscriptionState.Rejected;
                    break;

                default:
                    this.MessageReceived?.Invoke(new ActionCableMessage(message.Message, this.client.JsonSerializerOptions));
                    break;
            }
        }
    }
}
