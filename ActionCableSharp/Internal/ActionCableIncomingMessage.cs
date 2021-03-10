using System.Text.Json;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// Represents an incoming Action Cable message.
    /// </summary>
    internal readonly struct ActionCableIncomingMessage
    {
        /// <summary>
        /// Gets the type of message.
        /// </summary>
        public MessageType Type { get; init; }

        /// <summary>
        /// Gets this message's identifier.
        /// </summary>
        public string Identifier { get; init; }

        /// <summary>
        /// Gets the reason for disconnecting. Only present if <see cref="Type"/> is <see cref="MessageType.Disconnect"/>.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Gets whether the server allows reconnection or not. Only present if <see cref="Type"/> is <see cref="MessageType.Disconnect"/>.
        /// </summary>
        public bool? Reconnect { get; init; }

        /// <summary>
        /// Gets the data associated to this message.
        /// </summary>
        public JsonElement Message { get; init; }
    }
}
