using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// <see cref="IWebSocketFactory" /> that creates <see cref="ClientWebSocketWrapper"/> instances (wrapped <see cref="ClientWebSocket"/> instances). Contains no logic.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class ClientWebSocketFactory : IWebSocketFactory
    {
        /// <summary>
        /// Creates a <see cref="ClientWebSocket"/> wrapped in a <see cref="ClientWebSocketWrapper"/>.
        /// </summary>
        /// <returns>A new <see cref="ClientWebSocketWrapper"/>.</returns>
        public IWebSocket CreateWebSocket()
        {
            return new ClientWebSocketWrapper(new ClientWebSocket());
        }
    }
}
