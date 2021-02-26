using System.Net.WebSockets;

namespace ActionCableSharp.Internal
{
    internal class ClientWebSocketFactory : IWebSocketFactory
    {
        public IWebSocket CreateWebSocket()
        {
            return new ClientWebSocketWrapper(new ClientWebSocket());
        }
    }
}
