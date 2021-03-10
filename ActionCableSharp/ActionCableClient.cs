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
        private readonly SequentialTaskRunner sendTaskRunner;

        private IWebSocket? webSocket;
        private CancellationTokenSource? loopCancellationTokenSource;

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
            this.sendTaskRunner = new SequentialTaskRunner();
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
        /// <returns>A <see cref="Task"/> that completes once the client is connected.</returns>
        public Task ConnectAsync()
        {
            return this.ReconnectAsync(true);
        }

        /// <summary>
        /// Closes the WebSocket connection.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once the client is disconnected.</returns>
        public async Task DisconnectAsync()
        {
            if (this.webSocket?.IsConnected != true)
            {
                return;
            }

            await this.webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None).ConfigureAwait(false);

            this.State = ClientState.Disconnected;
        }

        /// <summary>
        /// Subscribes to a specific Action Cable channel.
        /// </summary>
        /// <param name="identifier">Identifier for the channel.</param>
        /// <returns>A reference to the <see cref="ActionCableSubscription"/> linked to the specified <paramref name="identifier"/>.</returns>
        public async Task<ActionCableSubscription> Subscribe(Identifier identifier)
        {
            var subscription = new ActionCableSubscription(this, identifier);
            this.subscriptions.Add(subscription);

            await this.EnqueueCommand("subscribe", identifier);

            return subscription;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.webSocket?.Dispose();
        }

        /// <summary>
        /// Enqueue a command to be sent to the server.
        /// </summary>
        /// <param name="command">Name of the command.</param>
        /// <param name="identifier"><see cref="Identifier"/> to use.</param>
        /// <param name="data">Optional additional data.</param>
        /// <returns>A <see cref="Task"/> that completes once the message has been sent to the server.</returns>
        internal Task EnqueueCommand(string command, Identifier identifier, object? data = null)
        {
            var message = new ActionCableOutgoingMessage
            {
                Command = command,
                Identifier = JsonSerializer.Serialize(identifier, identifier.GetType(), this.JsonSerializerOptions),
                Data = data != null ? JsonSerializer.Serialize(data, this.JsonSerializerOptions) : null,
            };

            return this.sendTaskRunner.Enqueue(() => this.SendMessage(message, CancellationToken.None));
        }

        /// <summary>
        /// Unsubscribe from a given <see cref="ActionCableSubscription"/>.
        /// </summary>
        /// <param name="subscription"><see cref="ActionCableSubscription"/> from which to unsubscribe.</param>
        /// <returns>A <see cref="Task"/> that completes once the unsubscription request has been sent to the server.</returns>
        internal async Task Unsubscribe(ActionCableSubscription subscription)
        {
            await this.EnqueueCommand("unsubscribe", subscription.Identifier);
            this.subscriptions.Remove(subscription);
        }

        private async Task ReconnectAsync(bool initial = false)
        {
            int reconnectDelayIndex = 0;

            while (this.webSocket?.IsConnected != true)
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
                    await this.webSocket.ConnectAsync(this.Uri, CancellationToken.None).ConfigureAwait(false);

                    this.logger.LogInformation($"Connected to {this.Uri}");

                    this.loopCancellationTokenSource = new CancellationTokenSource();

                    // don't await these since they run until the connection is closed/interrupted
                    _ = Task.Factory.StartNew(this.ReceiveLoop, TaskCreationOptions.LongRunning);

                    foreach (var subscription in this.subscriptions)
                    {
                        _ = this.EnqueueCommand("subscribe", subscription.Identifier);
                    }
                }
                catch (WebSocketException)
                {
                    int reconnectDelay = ReconnectDelays[reconnectDelayIndex];
                    this.logger.LogError($"Failed to connect, waiting {reconnectDelay} ms before retrying...");
                    await Task.Delay(reconnectDelay).ConfigureAwait(false);
                    reconnectDelayIndex = Math.Min(reconnectDelayIndex + 1, ReconnectDelays.Length - 1);
                }
            }
        }

        private async Task SendMessage(ActionCableOutgoingMessage message, CancellationToken cancellationToken)
        {
            if (this.webSocket == null)
            {
                throw new InvalidOperationException("WebSocket has not been initialized");
            }

            using var stream = new MemoryStream();

            await JsonSerializer.SerializeAsync(stream, message, this.JsonSerializerOptions, cancellationToken).ConfigureAwait(false);

            stream.Position = 0;

            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await this.webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Text, stream.Position >= stream.Length - 1, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop()
        {
            this.logger.LogInformation($"Started incoming message task");

            try
            {
                while (this.loopCancellationTokenSource?.IsCancellationRequested == false && this.webSocket?.IsConnected == true)
                {
                    using var stream = new MemoryStream();
                    var buffer = new byte[BufferSize];
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await this.webSocket.ReceiveAsync(buffer, this.loopCancellationTokenSource.Token).ConfigureAwait(false);
                        stream.Write(new ArraySegment<byte>(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    stream.Position = 0;

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            try
                            {
                                _ = this.ProcessMessage(stream);
                            }
                            catch (JsonException ex)
                            {
                                this.logger.LogError("Failed to process message: " + ex);
                            }

                            break;

                        case WebSocketMessageType.Close:
                            this.logger.LogInformation("Connection closed by remote host");
                            break;

                        default:
                            this.logger.LogWarning("Unexpected message type " + result.MessageType);
                            break;
                    }
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
        }

        private async Task ProcessMessage(Stream stream)
        {
            ActionCableIncomingMessage? message = await JsonSerializer.DeserializeAsync<ActionCableIncomingMessage>(stream, this.JsonSerializerOptions);

            if (!message.HasValue)
            {
                return;
            }

            switch (message.Value.Type)
            {
                case MessageType.Welcome:
                    this.State = ClientState.Connected;
                    break;

                case MessageType.Disconnect:
                    this.State = ClientState.Disconnected;
                    break;

                case MessageType.Ping:
                    break;

                case MessageType.Confirmation:
                case MessageType.Rejection:
                case MessageType.None:
                    foreach (var subscription in this.subscriptions)
                    {
                        subscription.HandleMessage(message.Value);
                    }

                    break;
            }
        }

        private async Task HandleWebSocketException(WebSocketException exception, string message)
        {
            this.logger.LogError(exception, message);
            this.loopCancellationTokenSource?.Cancel();
            this.State = ClientState.Reconnecting;
            await this.ReconnectAsync().ConfigureAwait(false);
        }
    }
}
