using System;
using System.Text.Json.Serialization;

namespace ActionCableSharp
{
    public class Identifier : IEquatable<Identifier>
    {
        [JsonPropertyName("channel")]
        public string ChannelName { get; }

        public Identifier(string channelName)
        {
            ChannelName = channelName;
        }

        public virtual bool Equals(Identifier? other)
        {
            if (other == null) return false;

            return ChannelName == other.ChannelName;
        }
    }
}
