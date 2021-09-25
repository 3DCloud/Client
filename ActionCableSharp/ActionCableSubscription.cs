using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;

namespace ActionCableSharp
{
    /// <summary>
    /// Encapsulates a subscription created when subscribing to a channel through an <see cref="ActionCableClient"/> instance.
    /// </summary>
    public class ActionCableSubscription : IDisposable
    {
        private readonly ActionCableClient client;
        private readonly Dictionary<string, List<Delegate>> callbacks = new();

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

            this.client.Connected += this.Client_Connected;
            this.client.Disconnected += this.Client_Disconnected;
            this.client.MessageReceived += this.Client_MessageReceived;
        }

        /// <summary>
        /// Event invoked when the server confirms the subscription.
        /// </summary>
        public event Action? Connected;

        /// <summary>
        /// Event invoked when the server rejects the subscription.
        /// </summary>
        public event Action? Rejected;

        /// <summary>
        /// Event invoked when the subscription is no longer active.
        /// </summary>
        public event Action? Disconnected;

        /// <summary>
        /// Event invoked when a message is received.
        /// </summary>
        public event Action<JsonElement>? Received;

        /// <summary>
        /// Gets the <see cref="Identifier"/> used to identifiy this subscription when communicating with the server.
        /// </summary>
        public Identifier Identifier { get; }

        /// <summary>
        /// Gets the subscription's current state.
        /// </summary>
        public SubscriptionState State { get; internal set; }

        /// <summary>
        /// Perform an action on the server.
        /// </summary>
        /// <param name="data"><see cref="ActionMessage"/> that contains the method name and optional data.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent.</returns>
        public Task PerformAsync(ActionMessage data, CancellationToken cancellationToken)
        {
            if (this.State != SubscriptionState.Subscribed) throw new InvalidOperationException("Not subscribed");

            return this.client.SendMessageAsync("message", this.Identifier, cancellationToken, data);
        }

        /// <summary>
        /// Subscribes this subscription on the server.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent to the server.</returns>
        public async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            if (this.client.State == ClientState.Connected)
            {
                await this.client.SendMessageAsync("subscribe", this.Identifier, cancellationToken).ConfigureAwait(false);
            }

            this.State = SubscriptionState.Pending;
        }

        /// <summary>
        /// Registers a callback when a message with an "action" field with the value <paramref name="actionName"/> is received.
        /// </summary>
        /// <param name="actionName">Name of the action.</param>
        /// <param name="callback">Callback to call.</param>
        public void RegisterCallback(string actionName, Action callback)
        {
            if (!this.callbacks.ContainsKey(actionName))
            {
                this.callbacks.Add(actionName, new List<Delegate>());
            }

            this.callbacks[actionName].Add(callback);
        }

        /// <summary>
        /// Registers a callback when a message with an "action" field with the value <paramref name="actionName"/> is received.
        /// </summary>
        /// <typeparam name="T">Type to which the message should be deserialized.</typeparam>
        /// <param name="actionName">Name of the action.</param>
        /// <param name="callback">Callback to call.</param>
        public void RegisterCallback<T>(string actionName, Action<T> callback)
        {
            if (!this.callbacks.ContainsKey(actionName))
            {
                this.callbacks.Add(actionName, new List<Delegate>());
            }

            this.callbacks[actionName].Add(callback);
        }

        /// <summary>
        /// Unsubscribe this subscription on the server.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the unsubscription request has been sent to the server.</returns>
        public async Task Unsubscribe(CancellationToken cancellationToken)
        {
            if (this.client.State == ClientState.Connected)
            {
                await this.client.SendMessageAsync("unsubscribe", this.Identifier, cancellationToken).ConfigureAwait(false);
            }

            this.State = SubscriptionState.Unsubscribed;
            this.Disconnected?.Invoke();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.client != null)
            {
                this.client.Connected -= this.Client_Connected;
                this.client.Disconnected -= this.Client_Disconnected;
                this.client.MessageReceived -= this.Client_MessageReceived;
            }

            if (this.State == SubscriptionState.Unsubscribed) return;

            this.State = SubscriptionState.Unsubscribed;
            this.Disconnected?.Invoke();
            this.callbacks.Clear();
        }

        private async void Client_Connected()
        {
            await this.SubscribeAsync(CancellationToken.None);
        }

        private async void Client_Disconnected()
        {
            await this.Unsubscribe(CancellationToken.None);
        }

        /// <summary>
        /// Handle a message received by the client.
        /// </summary>
        /// <param name="message">Received message.</param>
        private void Client_MessageReceived(ActionCableIncomingMessage message)
        {
            Identifier? identifier = (Identifier?)JsonSerializer.Deserialize(message.Identifier, this.Identifier.GetType(), this.client.JsonSerializerOptions);

            if (!this.Identifier.Equals(identifier)) return;

            switch (message.Type)
            {
                case MessageType.ConfirmSubscription:
                    this.State = SubscriptionState.Subscribed;
                    this.Connected?.Invoke();
                    break;

                case MessageType.RejectSubscription:
                    this.State = SubscriptionState.Rejected;
                    this.Rejected?.Invoke();
                    break;

                default:
                    this.Received?.Invoke(message.Message);
                    this.InvokeCallbacks(message.Message);
                    break;
            }
        }

        private void InvokeCallbacks(JsonElement message)
        {
            if (!message.TryGetProperty("action", out JsonElement actionValue)) return;

            string? action = actionValue.GetString();

            if (string.IsNullOrEmpty(action) || !this.callbacks.TryGetValue(action, out List<Delegate>? delegates)) return;

            foreach (Delegate del in delegates)
            {
                ParameterInfo[] parameterTypes = del.Method.GetParameters();

                if (parameterTypes.Length == 1)
                {
                    Type deserializeToType = del.Method.GetParameters()[0].ParameterType;
                    del.Method.Invoke(del.Target, new object?[] { this.ConvertToObject(message, deserializeToType) });
                }
                else
                {
                    del.Method.Invoke(del.Target, null);
                }
            }
        }

        private object? ConvertToObject(JsonElement jsonElement, Type type)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                jsonElement.WriteTo(writer);
            }

            return JsonSerializer.Deserialize(bufferWriter.WrittenSpan, type, this.client.JsonSerializerOptions);
        }
    }
}
