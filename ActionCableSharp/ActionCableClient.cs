using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActionCableSharp
{
    /// <summary>
    /// A simple client for the Action Cable v1 protocol.
    /// </summary>
    public class ActionCableClient : IDisposable
    {
        private const int BufferSize = 8192;
        private static readonly int[] ReconnectDelays = { 1_000, 2_000, 5_000, 10_000, 15_000, 20_000, 30_000 };

        private readonly ILogger<ActionCableClient> logger;
        private readonly IWebSocketFactory webSocketFactory;

        private readonly SemaphoreSlim sendMessageSemaphore = new(1);
        private readonly Random random = new();
        private readonly Dictionary<Identifier, ActionCableSubscription> subscriptions = new();

        private IWebSocket? webSocket;
        private CancellationTokenSource? loopCancellationTokenSource;
        private bool shouldReconnectAfterClose;
        private string? disconnectReason;

        private Task? receiveLoopTask;

        private bool connecting;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        public ActionCableClient(Uri uri, string origin)
            : this(new NullLogger<ActionCableClient>(), uri, origin, new ClientWebSocketFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="logger">The logger factory to use.</param>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        public ActionCableClient(ILogger<ActionCableClient> logger, Uri uri, string origin)
            : this(logger, uri, origin, new ClientWebSocketFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        /// <param name="webSocketFactory">Factory to use when creating <see cref="IWebSocket"/> instances.</param>
        internal ActionCableClient(Uri uri, string origin, IWebSocketFactory webSocketFactory)
            : this(new NullLogger<ActionCableClient>(), uri, origin, webSocketFactory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        /// <param name="webSocketFactory">Factory to use when creating <see cref="IWebSocket"/> instances.</param>
        internal ActionCableClient(ILogger<ActionCableClient> logger, Uri uri, string origin, IWebSocketFactory webSocketFactory)
        {
            this.Uri = uri;
            this.Origin = origin;

            SnakeCaseNamingPolicy namingPolicy = new();

            this.JsonSerializerOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters =
                {
                    new JsonStringEnumMemberConverter(namingPolicy),
                },
                PropertyNamingPolicy = namingPolicy,
            };

            this.logger = logger;
            this.webSocketFactory = webSocketFactory;
        }

        /// <summary>
        /// Represents a method that will handle the <see cref="Connected"/> event.
        /// </summary>
        public delegate void ConnectedHandler();

        /// <summary>
        /// Represents a method that will handle the <see cref="Disconnected"/> event.
        /// </summary>
        /// <param name="willReconnect">Indicates whether the client will attempt to reconnect or not.</param>
        /// <param name="reason">The reason for which the server disconnected (if applicable).</param>
        public delegate void DisconnectedHandler(bool willReconnect, string? reason);

        /// <summary>
        /// Represents a method that will handle the <see cref="MessageReceived"/> event.
        /// </summary>
        /// <param name="message">The raw received message.</param>
        internal delegate void MessageReceivedHandler(ActionCableIncomingMessage message);

        /// <summary>
        /// Event invoked when the client connects successfully to the server.
        /// </summary>
        public event ConnectedHandler? Connected;

        /// <summary>
        /// Event invoked when the client disconnects from the server.
        /// </summary>
        public event DisconnectedHandler? Disconnected;

        /// <summary>
        /// Event invoked when a subscription-bound message is received.
        /// </summary>
        internal virtual event MessageReceivedHandler? MessageReceived;

        /// <summary>
        /// Gets the URI to Action Cable mount path.
        /// </summary>
        public virtual Uri Uri { get; }

        /// <summary>
        /// Gets the origin to use in the headers of requests. This should be in Action Cable's allowed_request_origins configuration option.
        /// </summary>
        public virtual string Origin { get; }

        /// <summary>
        /// Gets the options used by the JSON serializer that reads/writes messages to the WebSocket.
        /// </summary>
        public virtual JsonSerializerOptions JsonSerializerOptions { get; }

        /// <summary>
        /// Gets the additional headers used when initiating a connection.
        /// </summary>
        public virtual List<(string HeaderName, string? HeaderValue)> AdditionalHeaders { get; } = new();

        /// <summary>
        /// Gets the current connection state of the client.
        /// </summary>
        public virtual ClientState State { get; private set; } = ClientState.Disconnected;

        /// <summary>
        /// Initiates the WebSocket connection.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the client is connected.</returns>
        public virtual Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(ActionCableClient));

            this.State = ClientState.Connecting;

            return this.ReconnectAsync(cancellationToken);
        }

        /// <summary>
        /// Closes the WebSocket connection.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the client is disconnected.</returns>
        public virtual Task DisconnectAsync(CancellationToken cancellationToken)
        {
            return this.DisconnectAsyncInternal(false, cancellationToken);
        }

        /// <summary>
        /// Subscribes to a specific Action Cable channel.
        /// </summary>
        /// <param name="identifier">Identifier for the channel.</param>
        /// <returns>A reference to the <see cref="ActionCableSubscription"/> linked to the specified <paramref name="identifier"/>.</returns>
        public virtual IActionCableSubscription GetSubscription(Identifier identifier)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableClient));
            }

            if (!this.subscriptions.TryGetValue(identifier, out ActionCableSubscription? subscription))
            {
                subscription = new ActionCableSubscription(this, identifier);
                this.subscriptions.Add(identifier, subscription);
            }

            return subscription;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Remove the specified subscription.
        /// </summary>
        /// <param name="subscription">The subscription to remove.</param>
        internal virtual void RemoveSubscription(ActionCableSubscription subscription)
        {
            this.subscriptions.Remove(subscription.Identifier);
        }

        /// <summary>
        /// Enqueue a command to be sent to the server.
        /// </summary>
        /// <param name="command">Name of the command.</param>
        /// <param name="identifier"><see cref="Identifier"/> to use.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent to the server.</returns>
        internal virtual async Task SendMessageAsync(string command, Identifier identifier, CancellationToken cancellationToken, object? data = null)
        {
            ActionCableOutgoingMessage message = new()
            {
                Command = command,
                Identifier = JsonSerializer.Serialize(identifier, identifier.GetType(), this.JsonSerializerOptions),
                Data = data != null ? JsonSerializer.Serialize(data, this.JsonSerializerOptions) : null,
            };

            if (this.webSocket?.IsConnected != true)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            await using MemoryStream stream = new();

            await JsonSerializer.SerializeAsync(stream, message, this.JsonSerializerOptions, cancellationToken).ConfigureAwait(false);

            stream.Position = 0;

            byte[] buffer = new byte[BufferSize];

            await this.sendMessageSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await this.webSocket.SendAsync(
                            new ArraySegment<byte>(buffer, 0, bytesRead),
                            WebSocketMessageType.Text,
                            stream.Position >= stream.Length - 1,
                            cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                this.sendMessageSemaphore.Release();
            }
        }

        /// <summary>
        /// Waits for and handles a single message received on the WebSocket.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once a message has been processed.</returns>
        internal virtual async Task ReceiveMessage(CancellationToken cancellationToken)
        {
            if (this.webSocket?.IsConnected != true)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            await using MemoryStream stream = new();
            byte[] buffer = new byte[BufferSize];
            WebSocketReceiveResult result;

            do
            {
                result = await this.webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                stream.Write(new ArraySegment<byte>(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            stream.Position = 0;

            switch (result.MessageType)
            {
                case WebSocketMessageType.Text:
                    try
                    {
                        await this.ProcessMessage(stream, cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException ex)
                    {
                        this.logger.LogError("Failed to process message\n{Exception}", ex);
                    }

                    break;

                case WebSocketMessageType.Close:
                    this.logger.LogInformation("Connection closed by remote host");

                    await this.webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by request from server", cancellationToken).ConfigureAwait(false);

                    break;

                default:
                    this.logger.LogWarning("Unexpected message type " + result.MessageType);
                    break;
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="ActionCableClient"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.webSocket?.Dispose();
            }

            this.State = ClientState.Disconnected;
            this.Disconnected?.Invoke(false, null);

            this.disposed = true;
        }

        private async Task DisconnectAsyncInternal(bool willReconnect, CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(ActionCableClient));
            }

            if (this.State is ClientState.Disconnecting or ClientState.Disconnected)
            {
                throw new InvalidOperationException("Client isn't connected");
            }

            this.State = ClientState.Disconnecting;

            if (this.webSocket is { IsConnected: true })
            {
                await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken).ConfigureAwait(false);
            }

            if (this.receiveLoopTask != null)
            {
                try
                {
                    await this.receiveLoopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            this.State = ClientState.Disconnected;
            this.Disconnected?.Invoke(willReconnect, this.disconnectReason);
        }

        private async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            if (this.connecting)
            {
                throw new InvalidOperationException("Already attempting to connect");
            }

            this.connecting = true;
            int reconnectDelayIndex = 0;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    this.logger.LogInformation("Connecting to '{CableUri}'...", this.Uri);

                    this.webSocket = this.webSocketFactory.CreateWebSocket();
                    this.webSocket.SetRequestHeader("Origin", this.Origin);

                    foreach ((string headerName, string? headerValue) in this.AdditionalHeaders)
                    {
                        this.webSocket.SetRequestHeader(headerName, headerValue);
                    }

                    await this.webSocket.ConnectAsync(this.Uri, cancellationToken).ConfigureAwait(false);

                    this.logger.LogInformation("Connected to {uri}", this.Uri);

                    this.State = ClientState.WaitingForWelcome;

                    this.loopCancellationTokenSource = new CancellationTokenSource();
                    this.shouldReconnectAfterClose = false;

                    this.receiveLoopTask = Task.Run(() => this.ReceiveLoop(this.loopCancellationTokenSource.Token).ContinueWith(this.HandleReceiveLoopTaskCompleted, CancellationToken.None), cancellationToken);
                }
                catch (WebSocketException)
                {
                    int reconnectDelay = ReconnectDelays[reconnectDelayIndex];
                    this.logger.LogError("Failed to connect, waiting {ReconnectDelay} ms before retrying...", reconnectDelay);

                    // prevent thundering herd problem by introducing random jitter
                    int actualDelay = this.random.Next((int)(reconnectDelay * 0.8), (int)(reconnectDelay * 1.2));
                    await Task.Delay(actualDelay, cancellationToken).ConfigureAwait(false);
                    reconnectDelayIndex = Math.Min(reconnectDelayIndex + 1, ReconnectDelays.Length - 1);
                }
            }
            while (this.webSocket?.IsConnected != true);

            this.connecting = false;
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"Started incoming message task");

            while (this.webSocket?.IsConnected == true)
            {
                await this.ReceiveMessage(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ProcessMessage(Stream stream, CancellationToken cancellationToken)
        {
            ActionCableIncomingMessage message = await JsonSerializer.DeserializeAsync<ActionCableIncomingMessage>(stream, this.JsonSerializerOptions, cancellationToken).ConfigureAwait(false);

            switch (message.Type)
            {
                case MessageType.Welcome:
                    this.State = ClientState.Connected;
                    this.Connected?.Invoke();
                    break;

                case MessageType.Disconnect:
                    if (!string.IsNullOrWhiteSpace(message.Reason))
                    {
                        this.logger.LogInformation("Server requested disconnect; reason: {reason}", message.Reason);
                        this.disconnectReason = message.Reason;
                    }
                    else
                    {
                        this.logger.LogInformation("Server requested disconnect");
                    }

                    this.shouldReconnectAfterClose = message.Reconnect == true;

                    break;

                case MessageType.ConfirmSubscription:
                case MessageType.RejectSubscription:
                case MessageType.None:
                    this.MessageReceived?.Invoke(message);
                    break;
            }
        }

        private async Task HandleReceiveLoopTaskCompleted(Task task)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogInformation("Incoming message task ended");
            }
            else if (task.IsFaulted)
            {
                this.logger.LogError("Incoming message task errored\n{Exception}", task.Exception);
            }
            else if (task.IsCanceled)
            {
                this.logger.LogWarning($"Incoming message task canceled");
            }

            bool reconnect = this.shouldReconnectAfterClose ||
                             task.Exception?.InnerExceptions.Any(ex => ex is WebSocketException) == true;

            await this.DisconnectAsyncInternal(reconnect, CancellationToken.None);

            if (reconnect)
            {
                await this.ReconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
