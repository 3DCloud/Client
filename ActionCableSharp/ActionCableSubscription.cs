using ActionCableSharp.Internal;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace ActionCableSharp
{
    public class ActionCableSubscription
    {
        public Identifier Identifier { get; }
        public SubscriptionState State { get; set; }

        public Action<ActionCableMessage>? MessageReceived;

        private readonly ActionCableClient client;

        internal ActionCableSubscription(ActionCableClient client, Identifier identifier)
        {
            Identifier = identifier;
            State = SubscriptionState.Pending;

            this.client = client;
        }

        public Task Perform(ActionMessage data)
        {
            return client.EnqueueCommand("message", Identifier, data);
        }

        public async Task Unsubscribe()
        {
            await client.Unsubscribe(this);

            State = SubscriptionState.Unsubscribed;
        }

        internal void HandleMessage(ActionCableIncomingMessage message)
        {
            Identifier? identifier = (Identifier?)JsonSerializer.Deserialize(message.Identifier, Identifier.GetType(), client.JsonSerializerOptions);

            if (!Identifier.Equals(identifier)) return;
            
            switch (message.Type)
            {
                case MessageType.Confirmation:
                    State = SubscriptionState.Subscribed;
                    break;

                case MessageType.Rejection:
                    State = SubscriptionState.Rejected;
                    break;

                default:
                    MessageReceived?.Invoke(new ActionCableMessage(message.Message, client.JsonSerializerOptions));
                    break;
            }
        }
    }
}
