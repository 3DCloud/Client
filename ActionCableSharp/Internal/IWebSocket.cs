using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp.Internal
{
    /// <summary>
    /// Represents a WebSocket. API is based on what <see cref="ClientWebSocket"/> offers.
    /// </summary>
    internal interface IWebSocket : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether or not this WebSocket is connected to a server.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the specified URI as an asynchronous operation.
        /// </summary>
        /// <param name="uri">The URI of the WebSocket server to connect to.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        /// <summary>
        /// Closes the WebSocket as an asynchronous operation.
        /// </summary>
        /// <param name="closeStatus">The WebSocket close status.</param>
        /// <param name="statusDescription">A description of the close status.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);

        /// <summary>
        /// Close the output for the WebSocket instance as an asynchronous operation.
        /// </summary>
        /// <param name="closeStatus">The WebSocket close status.</param>
        /// <param name="statusDescription">A description of the close status.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);

        /// <summary>
        /// Receives data from the WebSocket as an asynchronous operation.
        /// </summary>
        /// <param name="buffer">The region of memory to receive the response.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

        /// <summary>
        /// Sends data over the WebSocket as an asynchronous operation.
        /// </summary>
        /// <param name="buffer">The buffer containing the message to be sent.</param>
        /// <param name="messageType">One of the enumeration values that specifies whether the buffer is clear text or in a binary format.</param>
        /// <param name="endOfMessage">true to indicate this is the final asynchronous send; otherwise, false.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);

        /// <summary>
        /// Creates a HTTP request header that will be sent along with the connection request.
        /// </summary>
        /// <param name="headerName">The name of the HTTP header.</param>
        /// <param name="headerValue">The value of the HTTP header.</param>
        void SetRequestHeader(string headerName, string? headerValue);
    }
}
