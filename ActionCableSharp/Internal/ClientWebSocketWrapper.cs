using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    internal class ClientWebSocketWrapper : IWebSocket
    {
        public bool IsConnected => webSocket?.State == WebSocketState.Open;

        private ClientWebSocket webSocket;

        public ClientWebSocketWrapper(ClientWebSocket webSocket)
        {
            this.webSocket = webSocket;
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            return webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            return webSocket.ConnectAsync(uri, cancellationToken);
        }

        public void Dispose()
        {
            webSocket.Dispose();
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return webSocket.ReceiveAsync(buffer, cancellationToken);
        }

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            return webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        }

        public void SetRequestHeader(string headerName, string? headerValue)
        {
            webSocket.Options.SetRequestHeader(headerName, headerValue);
        }
    }
}
