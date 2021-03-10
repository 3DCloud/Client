namespace ActionCableSharp
{
    /// <summary>
    /// Represents the state of an <see cref="ActionCableClient"/>.
    /// </summary>
    public enum ClientState
    {
        /// <summary>
        /// The client is disconnected from the server.
        /// </summary>
        Disconnected,

        /// <summary>
        /// The client is attempting an initial connection to the server.
        /// </summary>
        Pending,

        /// <summary>
        /// The client is connected to the server.
        /// </summary>
        Connected,

        /// <summary>
        /// The client is attempting to reconnect after losing its connection to the server.
        /// </summary>
        Reconnecting,
    }
}
