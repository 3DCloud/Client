namespace ActionCableSharp
{
    /// <summary>
    /// Handles messages received through a <see cref="ActionCableSubscription"/>.
    /// </summary>
    public interface IMessageReceiver
    {
        /// <summary>
        /// Invoked when the subscription has been confirmed by the server.
        /// </summary>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> that triggered this method invocation.</param>
        void Subscribed(ActionCableSubscription subscription);

        /// <summary>
        /// Invoked when the subscription has been rejected by the server.
        /// </summary>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> that triggered this method invocation.</param>
        void Rejected(ActionCableSubscription subscription);

        /// <summary>
        /// Invoked when the subscription has been removed.
        /// </summary>
        void Unsubscribed();
    }
}
