using System;
using System.Text.Json.Serialization;

namespace ActionCableSharp
{
    /// <summary>
    /// Represents the indentification used when subscribing to a channel.
    /// </summary>
    public record Identifier
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Identifier"/> class.
        /// </summary>
        /// <param name="channelName">The name of the channel identified by this instance.</param>
        public Identifier(string channelName)
        {
            this.ChannelName = channelName;
        }

        /// <summary>
        /// Gets the name of the channel.
        /// </summary>
        [JsonPropertyName("channel")]
        public string ChannelName { get; }
    }
}
