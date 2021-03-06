﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// An <see cref="IWebSocket"/> wrapper for the built-in <see cref="ClientWebSocket"/> since the latter can't be mocked. Contains no logic.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class ClientWebSocketWrapper : IWebSocket
    {
        private readonly ClientWebSocket webSocket;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientWebSocketWrapper"/> class.
        /// </summary>
        /// <param name="webSocket"><see cref="ClientWebSocket"/> instance to wrap.</param>
        public ClientWebSocketWrapper(ClientWebSocket webSocket)
        {
            this.webSocket = webSocket;
        }

        /// <inheritdoc/>
        public bool IsConnected => this.webSocket?.State == WebSocketState.Open;

        /// <inheritdoc/>
        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            this.webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);

        /// <inheritdoc/>
        public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            this.webSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

        /// <inheritdoc/>
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            this.webSocket.ConnectAsync(uri, cancellationToken);

        /// <inheritdoc/>
        public void Dispose() =>
            this.webSocket.Dispose();

        /// <inheritdoc/>
        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            this.webSocket.ReceiveAsync(buffer, cancellationToken);

        /// <inheritdoc/>
        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            this.webSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

        /// <inheritdoc/>
        public void SetRequestHeader(string headerName, string? headerValue) =>
            this.webSocket.Options.SetRequestHeader(headerName, headerValue);
    }
}
