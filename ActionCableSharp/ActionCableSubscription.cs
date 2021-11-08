using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
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
    public class ActionCableSubscription : IActionCableSubscription
    {
        private readonly ActionCableClient client;
        private readonly Dictionary<string, List<Delegate>> callbacks = new();

        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableSubscription"/> class.
        /// </summary>
        /// <param name="client">The <see cref="ActionCableClient"/> instance to which this subscription belongs.</param>
        /// <param name="identifier">The <see cref="Identifier"/> used to identify this subscription when communicating with the server.</param>
        internal ActionCableSubscription(ActionCableClient client, Identifier identifier)
        {
            this.Identifier = identifier;
            this.State = SubscriptionState.Pending;

            this.client = client;

            this.client.Connected += this.Client_Connected;
            this.client.Disconnected += this.Client_Disconnected;
            this.client.MessageReceived += this.Client_MessageReceived;
        }

        /// <inheritdoc/>
        public event Action? Subscribed;

        /// <inheritdoc/>
        public event Action? Rejected;

        /// <inheritdoc/>
        public event Action? Unsubscribed;

        /// <inheritdoc/>
        public event Action<JsonElement>? Received;

        /// <inheritdoc/>
        public Identifier Identifier { get; }

        /// <inheritdoc/>
        public SubscriptionState State { get; internal set; }

        /// <inheritdoc/>
        public Task PerformAsync(ActionMessage data, CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            if (this.State != SubscriptionState.Subscribed)
            {
                throw new InvalidOperationException("Not subscribed");
            }

            return this.client.SendMessageAsync("message", this.Identifier, cancellationToken, data);
        }

        /// <inheritdoc/>
        public async Task GuaranteePerformAsync(ActionMessage data, CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            bool sent = false;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await this.client.SendMessageAsync("message", this.Identifier, cancellationToken, data);
                    sent = true;
                }
                catch (WebSocketException)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            while (!sent);
        }

        /// <inheritdoc/>
        public async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            if (this.client.State == ClientState.Connected)
            {
                await this.client.SendMessageAsync("subscribe", this.Identifier, cancellationToken).ConfigureAwait(false);
            }

            this.State = SubscriptionState.Pending;
        }

        /// <inheritdoc/>
        public void RegisterCallback(string actionName, Action callback)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            if (!this.callbacks.ContainsKey(actionName))
            {
                this.callbacks.Add(actionName, new List<Delegate>());
            }

            this.callbacks[actionName].Add(callback);
        }

        /// <inheritdoc/>
        public void RegisterCallback<T>(string actionName, Action<T> callback)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            if (!this.callbacks.ContainsKey(actionName))
            {
                this.callbacks.Add(actionName, new List<Delegate>());
            }

            this.callbacks[actionName].Add(callback);
        }

        /// <inheritdoc/>
        public async Task Unsubscribe(CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableSubscription));
            }

            if (this.client.State == ClientState.Connected)
            {
                await this.client.SendMessageAsync("unsubscribe", this.Identifier, cancellationToken).ConfigureAwait(false);
            }

            if (this.State == SubscriptionState.Subscribed)
            {
                this.State = SubscriptionState.Unsubscribed;
                this.Unsubscribed?.Invoke();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ActionCableSubscription"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            if (disposing)
            {
                this.client.Connected -= this.Client_Connected;
                this.client.Disconnected -= this.Client_Disconnected;
                this.client.MessageReceived -= this.Client_MessageReceived;

                if (this.State != SubscriptionState.Unsubscribed)
                {
                    this.State = SubscriptionState.Unsubscribed;
                    this.Unsubscribed?.Invoke();
                }

                this.callbacks.Clear();
            }

            this.client.RemoveSubscription(this);

            this.disposed = true;
        }

        private async void Client_Connected()
        {
            await this.SubscribeAsync(CancellationToken.None);
        }

        private async void Client_Disconnected(bool isReconnecting)
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
                    this.Subscribed?.Invoke();
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
                    del.Method.Invoke(del.Target, new[] { this.ConvertToObject(message, deserializeToType) });
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

            using (Utf8JsonWriter writer = new(bufferWriter))
            {
                jsonElement.WriteTo(writer);
            }

            return JsonSerializer.Deserialize(bufferWriter.WrittenSpan, type, this.client.JsonSerializerOptions);
        }
    }
}
