using System.Net.WebSockets;

namespace ActionCableSharp.Internal
{
    internal interface IWebSocketFactory
    {
        IWebSocket CreateWebSocket();
    }
}
