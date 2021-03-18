namespace ActionCableSharp
{
    /// <summary>
    /// Handles messages received through a <see cref="ActionCableSubscription"/>.
    /// </summary>
    public abstract class MessageReceiver
    {
        /// <summary>
        /// Invoked when the subscription has been confirmed by the server.
        /// </summary>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> that triggered this method invocation.</param>
        public virtual void Subscribed(ActionCableSubscription subscription)
        {
        }

        /// <summary>
        /// Invoked when the subscription has been rejected by the server.
        /// </summary>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> that triggered this method invocation.</param>
        public virtual void Rejected(ActionCableSubscription subscription)
        {
        }

        /// <summary>
        /// Invoked when the subscription has been removed.
        /// </summary>
        public virtual void Unsubscribed()
        {
        }
    }
}
