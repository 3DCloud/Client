using System;
using ActionCableSharp;

namespace Client
{
    /// <summary>
    /// Identifies a 3DCloud client.
    /// </summary>
    internal class ClientIdentifier : Identifier
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClientIdentifier"/> class.
        /// </summary>
        /// <param name="guid">GUID that identifies this client.</param>
        /// <param name="key">The key used to authenticate the client when connecting to the server.</param>
        public ClientIdentifier(Guid guid, string key)
            : base("ClientsChannel")
        {
            this.Guid = guid;
            this.Key = key;
        }

        /// <summary>
        /// Gets the GUID that identifies this client.
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        /// Gets the key used to authenticate the client when connecting to the server.
        /// </summary>
        public string Key { get; }

        /// <inheritdoc/>
        public override bool Equals(Identifier? other)
        {
            if (other == null || other is not ClientIdentifier clientIdentifier) return false;

            return this.ChannelName == clientIdentifier.ChannelName && this.Guid == clientIdentifier.Guid && this.Key == clientIdentifier.Key;
        }
    }
}
