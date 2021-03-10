namespace ActionCableSharp.Internal
{
    /// <summary>
    /// Represents an outgoing Action Cable message.
    /// </summary>
    internal readonly struct ActionCableOutgoingMessage
    {
        /// <summary>
        /// Gets the command associated with this message.
        /// </summary>
        public string Command { get; init; }

        /// <summary>
        /// Gets the identifier associated with this message.
        /// </summary>
        public string Identifier { get; init; }

        /// <summary>
        /// Gets optional data to be sent along with the command.
        /// </summary>
        public string? Data { get; init; }
    }
}
