using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp
{
    /// <summary>
    /// Encapsulates a subscription created when subscribing to a channel through an <see cref="ActionCableClient"/> instance.
    /// </summary>
    public interface IActionCableSubscription : IDisposable
    {
        /// <summary>
        /// Event invoked when the server confirms the subscription.
        /// </summary>
        event Action? Subscribed;

        /// <summary>
        /// Event invoked when the server rejects the subscription.
        /// </summary>
        event Action? Rejected;

        /// <summary>
        /// Event invoked when the subscription is no longer active.
        /// </summary>
        event Action? Unsubscribed;

        /// <summary>
        /// Event invoked when a message is received.
        /// </summary>
        event Action<JsonElement>? Received;

        /// <summary>
        /// Gets the <see cref="Identifier"/> used to identify this subscription when communicating with the server.
        /// </summary>
        Identifier Identifier { get; }

        /// <summary>
        /// Gets the subscription's current state.
        /// </summary>
        SubscriptionState State { get; }

        /// <summary>
        /// Perform an action on the server.
        /// </summary>
        /// <param name="data"><see cref="ActionMessage"/> that contains the method name and optional data.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent.</returns>
        Task PerformAsync(ActionMessage data, CancellationToken cancellationToken);

        /// <summary>
        /// Perform an action on the server. If an error occurs, try again until the message is successfully sent.
        /// </summary>
        /// <param name="data"><see cref="ActionMessage"/> that contains the method name and optional data.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent.</returns>
        Task GuaranteePerformAsync(ActionMessage data, CancellationToken cancellationToken);

        /// <summary>
        /// Subscribes this subscription on the server.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent to the server.</returns>
        Task SubscribeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Registers a callback when a message with an "action" field with the value <paramref name="actionName"/> is received.
        /// </summary>
        /// <param name="actionName">Name of the action.</param>
        /// <param name="callback">Callback to call.</param>
        void RegisterCallback(string actionName, Action callback);

        /// <summary>
        /// Registers a callback when a message with an "action" field with the value <paramref name="actionName"/> is received.
        /// </summary>
        /// <typeparam name="T">Type to which the message should be deserialized.</typeparam>
        /// <param name="actionName">Name of the action.</param>
        /// <param name="callback">Callback to call.</param>
        void RegisterCallback<T>(string actionName, Action<T> callback);

        /// <summary>
        /// Unsubscribe this subscription on the server.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the unsubscription request has been sent to the server.</returns>
        Task Unsubscribe(CancellationToken cancellationToken);
    }
}
