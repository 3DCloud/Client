using ActionCableSharp;
using System;

namespace Client
{
    internal class ClientIdentifier : Identifier
    {
        public Guid Guid { get; }
        public string Key { get; }

        public ClientIdentifier(Guid guid, string key) : base("ClientsChannel")
        {
            Guid = guid;
            Key = key;
        }

        public override bool Equals(Identifier? other)
        {
            if (other == null || other is not ClientIdentifier clientIdentifier) return false;

            return ChannelName == clientIdentifier.ChannelName && Guid == clientIdentifier.Guid && Key == clientIdentifier.Key;
        }
    }
}
