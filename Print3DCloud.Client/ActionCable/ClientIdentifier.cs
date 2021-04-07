using System;
using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Identifies a 3DCloud client.
    /// </summary>
    internal record ClientIdentifier : Identifier
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClientIdentifier"/> class.
        /// </summary>
        /// <param name="id">GUID that identifies this client.</param>
        /// <param name="secret">The secret used to authenticate the client when connecting to the server.</param>
        public ClientIdentifier(Guid id, string? secret)
            : base("ClientChannel")
        {
            this.Id = id;
            this.Secret = secret;
        }

        /// <summary>
        /// Gets the GUID that identifies this client.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Gets the secret used to authenticate the client when connecting to the server.
        /// </summary>
        public string? Secret { get; }
    }
}
