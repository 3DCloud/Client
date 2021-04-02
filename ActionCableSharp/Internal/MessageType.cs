namespace ActionCableSharp.Internal
{
    /// <summary>
    /// The type of message represented by an instance of <see cref="ActionCableIncomingMessage"/>.
    /// </summary>
    internal enum MessageType
    {
        /// <summary>
        /// The message type could not be determined.
        /// </summary>
        None,

        /// <summary>
        /// The initial welcome message sent by the server.
        /// </summary>
        Welcome,

        /// <summary>
        /// The request by the server to end the connection.
        /// </summary>
        Disconnect,

        /// <summary>
        /// An empty ping message to ensure the connection stays alive.
        /// </summary>
        Ping,

        /// <summary>
        /// The message confirms a subscription.
        /// </summary>
        ConfirmSubscription,

        /// <summary>
        /// The message rejects a subscription.
        /// </summary>
        RejectSubscription,
    }
}
