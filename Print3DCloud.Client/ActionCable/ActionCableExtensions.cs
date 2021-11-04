using System;
using System.Threading;
using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Represents a method that is called when an acknowledgeable message is received.
    /// </summary>
    /// <param name="message">The received message.</param>
    /// <param name="ack">The function to call to acknowledge the message.</param>
    /// <typeparam name="T">The message type.</typeparam>
    public delegate void AcknowledgeableCallback<in T>(T message, AcknowledgeCallback ack)
        where T : AcknowledgeableMessage;

    /// <summary>
    /// Represents a method that is called to acknowledge a message.
    /// </summary>
    /// <param name="exception">The exception associated with this acknowledgement, if any.</param>
    public delegate void AcknowledgeCallback(Exception? exception = null);

    /// <summary>
    /// Various extension methods for <see cref="ActionCableSharp"/>.
    /// </summary>
    public static class ActionCableExtensions
    {
        /// <summary>
        /// Register a message callback that can be acknowledged.
        /// </summary>
        /// <param name="subscription">The subscription in which to register the callback.</param>
        /// <param name="actionName">The name of the action.</param>
        /// <param name="callback">The callback that will be called when the message is received.</param>
        /// <typeparam name="T">The message type. Must be a subclass of <see cref="AcknowledgeableMessage"/>.</typeparam>
        public static void RegisterAcknowledgeableCallback<T>(this IActionCableSubscription subscription, string actionName, AcknowledgeableCallback<T> callback)
            where T : AcknowledgeableMessage
        {
            subscription.RegisterCallback<T>(actionName, (message) =>
            {
                bool acknowledged = false;

                void Acknowledge(Exception? exception)
                {
                    if (acknowledged)
                    {
                        return;
                    }

                    acknowledged = true;
                    subscription.GuaranteePerformAsync(new AcknowledgeMessage(message.MessageId, exception), CancellationToken.None);
                }

                callback(message, Acknowledge);
            });
        }
    }
}