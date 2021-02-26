using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    internal interface IWebSocket : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
        Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
        Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
        void SetRequestHeader(string headerName, string? headerValue);
    }
}
