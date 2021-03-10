using System;
using System.Text.Json.Serialization;

namespace ActionCableSharp
{
    /// <summary>
    /// Represents the indentification used when subscribing to a channel.
    /// </summary>
    public class Identifier : IEquatable<Identifier>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Identifier"/> class.
        /// </summary>
        /// <param name="channelName">Name of the channel.</param>
        public Identifier(string channelName)
        {
            this.ChannelName = channelName;
        }

        /// <summary>
        /// Gets the name of the channel.
        /// </summary>
        [JsonPropertyName("channel")]
        public string ChannelName { get; }

        /// <summary>
        /// Indicates whether this <see cref="Identifier"/> is equal to another <see cref="Identifier"/>.
        /// </summary>
        /// <param name="other">The other <see cref="Identifier"/>.</param>
        /// <returns>Whether both <see cref="Identifier"/> instances are equal or not.</returns>
        public virtual bool Equals(Identifier? other)
        {
            if (other == null)
            {
                return false;
            }

            return this.ChannelName == other.ChannelName;
        }
    }
}
