namespace ActionCableSharp.Internal
{
    internal readonly struct ActionCableOutgoingMessage
    {
        public string Command { get; init; }
        public string Identifier { get; init; }
        public string? Data { get; init; }
    }
}
