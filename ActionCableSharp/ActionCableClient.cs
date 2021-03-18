using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Microsoft.Extensions.Logging;

namespace ActionCableSharp
{
    /// <summary>
    /// A simple client for the Action Cable v1 protocol.
    /// </summary>
    public class ActionCableClient : IDisposable
    {
        private const int BufferSize = 8192;
        private static readonly int[] ReconnectDelays = new int[] { 1_000, 2_000, 5_000, 10_000, 15_000, 20_000, 30_000 };

        private readonly ILogger<ActionCableClient> logger;
        private readonly IWebSocketFactory webSocketFactory;
        private readonly List<ActionCableSubscription> subscriptions;
        private readonly SemaphoreSlim semaphore;

        private IWebSocket? webSocket;
        private CancellationTokenSource? loopCancellationTokenSource;
        private bool shouldReconnectAfterClose;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        public ActionCableClient(Uri uri, string origin)
            : this(uri, origin, new ClientWebSocketFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        /// <param name="webSocketFactory">Factory to use when creating <see cref="IWebSocket"/> instances.</param>
        internal ActionCableClient(Uri uri, string origin, IWebSocketFactory webSocketFactory)
        {
            this.Uri = uri;
            this.Origin = origin;
            this.State = ClientState.Disconnected;

            var namingPolicy = new SnakeCaseNamingPolicy();

            this.JsonSerializerOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                DictionaryKeyPolicy = namingPolicy,
                Converters =
                {
                    new JsonStringEnumMemberConverter(namingPolicy),
                },
                PropertyNamingPolicy = namingPolicy,
            };

            this.logger = Logging.LoggerFactory.CreateLogger<ActionCableClient>();
            this.webSocketFactory = webSocketFactory;
            this.subscriptions = new List<ActionCableSubscription>();
            this.semaphore = new SemaphoreSlim(1);
        }

        /// <summary>
        /// Gets the URI to Action Cable mount path.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// Gets the origin to use in the headers of requests. This should be in Action Cable's allowed_request_origins configuration option.
        /// </summary>
        public string Origin { get; }

        /// <summary>
        /// Gets the options used by the JSON serializer that reads/writes messages to the WebSocket.
        /// </summary>
        public JsonSerializerOptions JsonSerializerOptions { get; }

        /// <summary>
        /// Gets the current connection state of the client.
        /// </summary>
        public ClientState State { get; private set; }

        /// <summary>
        /// Initiates the WebSocket connection.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the client is connected.</returns>
        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            return this.ReconnectAsync(cancellationToken, true);
        }

        /// <summary>
        /// Closes the WebSocket connection.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the client is disconnected.</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (this.webSocket?.IsConnected != true)
            {
                return;
            }

            await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken).ConfigureAwait(false);

            this.State = ClientState.Disconnected;
        }

        /// <summary>
        /// Subscribes to a specific Action Cable channel.
        /// </summary>
        /// <param name="identifier">Identifier for the channel.</param>
        /// <param name="receiver">The <see cref="MessageReceiver"/> that will receive messages.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A reference to the <see cref="ActionCableSubscription"/> linked to the specified <paramref name="identifier"/>.</returns>
        public async Task<ActionCableSubscription> Subscribe(Identifier identifier, MessageReceiver receiver, CancellationToken cancellationToken)
        {
            var subscription = new ActionCableSubscription(this, identifier, receiver);
            this.subscriptions.Add(subscription);

            try
            {
                await this.SendMessageAsync("subscribe", identifier, cancellationToken);
            }
            catch (WebSocketException ex)
            {
                this.logger.LogError(ex.ToString());
            }

            return subscription;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.State = ClientState.Disconnected;
            this.webSocket?.Dispose();
        }

        /// <summary>
        /// Enqueue a command to be sent to the server.
        /// </summary>
        /// <param name="command">Name of the command.</param>
        /// <param name="identifier"><see cref="Identifier"/> to use.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent to the server.</returns>
        internal async Task SendMessageAsync(string command, Identifier identifier, CancellationToken cancellationToken, object? data = null)
        {
            var message = new ActionCableOutgoingMessage
            {
                Command = command,
                Identifier = JsonSerializer.Serialize(identifier, identifier.GetType(), this.JsonSerializerOptions),
                Data = data != null ? JsonSerializer.Serialize(data, this.JsonSerializerOptions) : null,
            };

            if (this.webSocket?.IsConnected != true)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            using var stream = new MemoryStream();

            await JsonSerializer.SerializeAsync(stream, message, this.JsonSerializerOptions, cancellationToken).ConfigureAwait(false);

            stream.Position = 0;

            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            await this.semaphore.WaitAsync();

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await this.webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Text, stream.Position >= stream.Length - 1, cancellationToken).ConfigureAwait(false);
            }

            this.semaphore.Release();
        }

        /// <summary>
        /// Unsubscribe from a given <see cref="ActionCableSubscription"/>.
        /// </summary>
        /// <param name="subscription"><see cref="ActionCableSubscription"/> from which to unsubscribe.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the unsubscription request has been sent to the server.</returns>
        internal async Task Unsubscribe(ActionCableSubscription subscription, CancellationToken cancellationToken)
        {
            await this.SendMessageAsync("unsubscribe", subscription.Identifier, cancellationToken);
            this.subscriptions.Remove(subscription);
        }

        /// <summary>
        /// Waits for and handles a single message received on the WebSocket.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once a message has been processed.</returns>
        internal async Task ReceiveMessage(CancellationToken cancellationToken)
        {
            if (this.webSocket?.IsConnected != true)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            using var stream = new MemoryStream();
            var buffer = new byte[BufferSize];
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
                        await this.ProcessMessage(stream, cancellationToken);
                    }
                    catch (JsonException ex)
                    {
                        this.logger.LogError("Failed to process message");
                        this.logger.LogError(ex.ToString());
                    }

                    break;

                case WebSocketMessageType.Close:
                    this.logger.LogInformation("Connection closed by remote host");
                    await this.webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by request from server", cancellationToken);
                    break;

                default:
                    this.logger.LogWarning("Unexpected message type " + result.MessageType);
                    break;
            }
        }

        private async Task ReconnectAsync(CancellationToken cancellationToken, bool initial = false)
        {
            int reconnectDelayIndex = 0;

            while (this.webSocket?.IsConnected != true && !cancellationToken.IsCancellationRequested)
            {
                this.webSocket?.Dispose();
                this.webSocket = null;

                this.loopCancellationTokenSource?.Cancel();
                this.loopCancellationTokenSource = null;

                this.State = initial ? ClientState.Pending : ClientState.Reconnecting;

                try
                {
                    this.logger.LogInformation($"Connecting to {this.Uri}");

                    this.webSocket = this.webSocketFactory.CreateWebSocket();
                    this.webSocket.SetRequestHeader("Origin", this.Origin);
                    await this.webSocket.ConnectAsync(this.Uri, cancellationToken).ConfigureAwait(false);

                    this.logger.LogInformation($"Connected to {this.Uri}");

                    this.State = ClientState.WaitingForWelcome;

                    this.loopCancellationTokenSource = new CancellationTokenSource();
                    this.shouldReconnectAfterClose = false;

                    // don't await these since they run until the connection is closed/interrupted
                    _ = Task.Factory.StartNew(this.ReceiveLoop, TaskCreationOptions.LongRunning);

                    // these run sequentially so might as well await each one individually
                    foreach (var subscription in this.subscriptions)
                    {
                        await this.SendMessageAsync("subscribe", subscription.Identifier, cancellationToken);
                    }
                }
                catch (WebSocketException)
                {
                    int reconnectDelay = ReconnectDelays[reconnectDelayIndex];
                    this.logger.LogError($"Failed to connect, waiting {reconnectDelay} ms before retrying...");
                    await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
                    reconnectDelayIndex = Math.Min(reconnectDelayIndex + 1, ReconnectDelays.Length - 1);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            this.logger.LogInformation($"Started incoming message task");

            try
            {
                while (this.loopCancellationTokenSource?.IsCancellationRequested == false && this.webSocket?.IsConnected == true)
                {
                    await this.ReceiveMessage(this.loopCancellationTokenSource.Token);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (WebSocketException ex)
            {
                await this.HandleWebSocketException(ex, "Failed to receive message").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex.Message);
                this.logger.LogError(ex.StackTrace);
            }

            this.logger.LogInformation($"Incoming message task ended");

            if (this.shouldReconnectAfterClose)
            {
                await this.ReconnectAsync(CancellationToken.None);
            }
        }

        private async Task ProcessMessage(Stream stream, CancellationToken cancellationToken)
        {
            ActionCableIncomingMessage message = await JsonSerializer.DeserializeAsync<ActionCableIncomingMessage>(stream, this.JsonSerializerOptions, cancellationToken);

            switch (message.Type)
            {
                case MessageType.Welcome:
                    this.State = ClientState.Connected;
                    break;

                case MessageType.Disconnect:
                    if (!string.IsNullOrWhiteSpace(message.Reason))
                    {
                        this.logger.LogInformation("Server requested disconnect; reason: " + message.Reason);
                    }
                    else
                    {
                        this.logger.LogInformation("Server requested disconnect");
                    }

                    this.State = ClientState.Disconnecting;
                    this.shouldReconnectAfterClose = message.Reconnect == true;
                    break;

                case MessageType.ConfirmSubscription:
                case MessageType.RejectSubscription:
                case MessageType.None:
                    foreach (var subscription in this.subscriptions)
                    {
                        subscription.HandleMessage(message);
                    }

                    break;
            }
        }

        private async Task HandleWebSocketException(WebSocketException exception, string message)
        {
            this.logger.LogError(exception, message);
            this.loopCancellationTokenSource?.Cancel();
            this.State = ClientState.Reconnecting;
            await this.ReconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
