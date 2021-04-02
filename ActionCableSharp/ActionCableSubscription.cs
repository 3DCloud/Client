using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
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
    public class ActionCableSubscription
    {
        private readonly ActionCableClient client;
        private readonly IMessageReceiver receiver;
        private readonly Type receiverType;
        private readonly Dictionary<string, ActionMethod> actionMethods;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableSubscription"/> class.
        /// </summary>
        /// <param name="client">The <see cref="ActionCableClient"/> instance to which this subscription belongs.</param>
        /// <param name="identifier">The <see cref="Identifier"/> used to identifiy this subscription when communicating with the server.</param>
        /// <param name="receiver">The <see cref="IMessageReceiver"/> that will receive messages.</param>
        internal ActionCableSubscription(ActionCableClient client, Identifier identifier, IMessageReceiver receiver)
        {
            this.Identifier = identifier;
            this.State = SubscriptionState.Pending;

            this.client = client;
            this.receiver = receiver;
            this.receiverType = receiver.GetType();
            this.actionMethods = new Dictionary<string, ActionMethod>();

            this.UpdateActionMethods();
        }

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
            await this.client.Unsubscribe(this, cancellationToken).ConfigureAwait(false);

            this.State = SubscriptionState.Unsubscribed;

            this.receiver.Unsubscribed();
        }

        /// <summary>
        /// Handle a message received by the client.
        /// </summary>
        /// <param name="message">Received message.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been handled.</returns>
        internal Task HandleMessage(ActionCableIncomingMessage message)
        {
            Identifier? identifier = (Identifier?)JsonSerializer.Deserialize(message.Identifier, this.Identifier.GetType(), this.client.JsonSerializerOptions);

            if (!this.Identifier.Equals(identifier))
            {
                return Task.CompletedTask;
            }

            switch (message.Type)
            {
                case MessageType.ConfirmSubscription:
                    this.State = SubscriptionState.Subscribed;
                    this.receiver.Subscribed(this);
                    return Task.CompletedTask;

                case MessageType.RejectSubscription:
                    this.State = SubscriptionState.Rejected;
                    this.receiver.Rejected(this);
                    return Task.CompletedTask;

                default:
                    object? result = this.InvokeAction(message.Message);
                    return result is Task task ? task : Task.FromResult(result);
            }
        }

        private void UpdateActionMethods()
        {
            this.actionMethods.Clear();

            var namingPolicy = new SnakeCaseNamingPolicy();

            foreach (MethodInfo method in this.receiverType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => !m.IsSpecialName))
            {
                ActionMethodAttribute? attribute = method.GetCustomAttribute<ActionMethodAttribute>();

                // exclude methods that don't have an ActionMethodAttribute
                if (attribute == null) continue;

                string actionName;

                if (!string.IsNullOrEmpty(attribute.ActionName))
                {
                    actionName = attribute.ActionName;
                }
                else
                {
                    actionName = namingPolicy.ConvertName(method.Name);
                }

                this.actionMethods.TryAdd(actionName, new ActionMethod(method, method.GetParameters()));
            }
        }

        private object? InvokeAction(JsonElement data)
        {
            if (data.Equals(default))
            {
                throw new InvalidOperationException($"Data is empty");
            }

            if (!data.TryGetProperty("action", out JsonElement action))
            {
                throw new InvalidOperationException($"Action key does not exist in message");
            }

            string? actionName = action.GetString();

            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new InvalidOperationException($"Action is empty");
            }

            if (!this.actionMethods.TryGetValue(actionName, out ActionMethod? actionMethod))
            {
                throw new InvalidOperationException($"No method for action '{actionName}'");
            }

            var args = new List<object?>();

            foreach (ParameterInfo parameter in actionMethod.Parameters)
            {
                Type parameterType = parameter.ParameterType;

                if (parameterType == typeof(ActionCableSubscription))
                {
                    args.Add(this);
                }
                else if (parameterType == typeof(JsonElement))
                {
                    args.Add(data);
                }
                else
                {
                    var bufferWriter = new ArrayBufferWriter<byte>();

                    using (var writer = new Utf8JsonWriter(bufferWriter))
                    {
                        data.WriteTo(writer);
                    }

                    args.Add(JsonSerializer.Deserialize(bufferWriter.WrittenSpan, parameterType, this.client.JsonSerializerOptions));
                }
            }

            return actionMethod.Method.Invoke(this.receiver, args.ToArray());
        }

        private class ActionMethod
        {
            public ActionMethod(MethodInfo method, ParameterInfo[] parameters)
            {
                this.Method = method;
                this.Parameters = parameters;
            }

            public MethodInfo Method { get; }

            public ParameterInfo[] Parameters { get; }
        }
    }
}
