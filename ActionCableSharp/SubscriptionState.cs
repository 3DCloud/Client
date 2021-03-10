namespace ActionCableSharp
{
    /// <summary>
    /// State of an <see cref="ActionCableSubscription"/>.
    /// </summary>
    public enum SubscriptionState
    {
        /// <summary>
        /// Waiting for confirmation from the server that this subscription is active.
        /// </summary>
        Pending,

        /// <summary>
        /// Subscription has been confirmed by the server and may send/receive messages.
        /// </summary>
        Subscribed,

        /// <summary>
        /// The subscription was rejected by the server. No messages will be exchanged.
        /// </summary>
        Rejected,

        /// <summary>
        /// The subscription is no longer active.
        /// </summary>
        Unsubscribed,
    }
}
