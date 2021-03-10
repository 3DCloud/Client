using System.Net.WebSockets;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// Represents a class that can create <see cref="IWebSocket"/> instances.
    /// </summary>
    internal interface IWebSocketFactory
    {
        /// <summary>
        /// Creates an instance of a class that implements <see cref="IWebSocket"/>.
        /// </summary>
        /// <returns>An <see cref="IWebSocket"/> instance.</returns>
        IWebSocket CreateWebSocket();
    }
}
