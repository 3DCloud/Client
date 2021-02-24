using System.Text.Json;

namespace ActionCableSharp.Internal
{
    internal readonly struct ActionCableIncomingMessage
    {
        public MessageType Type { get; init; }
        public string Identifier { get; init; }
        public string Reason { get; init; }
        public string Reconnect { get; init; }
        public JsonElement Message { get; init; }
    }
}
