using ActionCableSharp.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ActionCableSharp
{
    /// <summary>
    /// A simple client for the Action Cable v1 protocol.
    /// </summary>
    public class ActionCableClient : IDisposable
    {
        private const int BufferSize = 8192;
        private static readonly int[] ReconnectDelays = new int[] { 1_000, 2_000, 5_000, 10_000, 15_000, 20_000, 30_000 };

        /// <summary>
        /// URI to Action Cable mount path.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// Origin to use in the headers of requests. This should be in Action Cable's <code>allowed_request_origins</code> configuration option.
        /// </summary>
        public string Origin { get; }

        /// <summary>
        /// Options used by the JSON serializer that reads/writes messages to the WebSocket.
        /// </summary>
        public JsonSerializerOptions JsonSerializerOptions { get; }

        /// <summary>
        /// Current connection state of the client.
        /// </summary>
        public ClientState State { get; private set; }

        private readonly ILogger<ActionCableClient> logger;
        private readonly IWebSocketFactory webSocketFactory;
        private readonly List<ActionCableSubscription> subscriptions;
        private readonly SequentialTaskRunner sendTaskRunner;

        private IWebSocket? webSocket;
        private CancellationTokenSource? loopCancellationTokenSource;

        /// <summary>
        /// Creates a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        public ActionCableClient(Uri uri, string origin) : this(uri, origin, new ClientWebSocketFactory()) { }

        internal ActionCableClient(Uri uri, string origin, IWebSocketFactory webSocketFactory)
        {
            Uri = uri;
            Origin = origin;
            State = ClientState.Disconnected;

            var namingPolicy = new SnakeCaseNamingPolicy();

            JsonSerializerOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                DictionaryKeyPolicy = namingPolicy,
                Converters =
                {
                    new JsonStringEnumMemberConverter(namingPolicy)
                },
                PropertyNamingPolicy = namingPolicy
            };

            logger = Logging.LoggerFactory.CreateLogger<ActionCableClient>();
            this.webSocketFactory = webSocketFactory;
            subscriptions = new List<ActionCableSubscription>();
            sendTaskRunner = new SequentialTaskRunner();
        }

        /// <summary>
        /// Initiates the WebSocket connection.
        /// </summary>
        public Task ConnectAsync()
        {
            return ReconnectAsync(true);
        }

        public async Task DisconnectAsync()
        {
            if (webSocket?.IsConnected != true) return;

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None).ConfigureAwait(false);

            State = ClientState.Disconnected;
        }

        /// <summary>
        /// Subscribes to a specific Action Cable channel.
        /// </summary>
        /// <param name="identifier">Identifier for the channel.</param>
        /// <returns>A reference to the <see cref="ActionCableSubscription"/> linked to the specified <paramref name="identifier"/>.</returns>
        public async Task<ActionCableSubscription> Subscribe(Identifier identifier)
        {
            var subscription = new ActionCableSubscription(this, identifier);
            subscriptions.Add(subscription);

            await EnqueueCommand("subscribe", identifier);

            return subscription;
        }

        public void Dispose()
        {
            webSocket?.Dispose();
        }

        internal Task EnqueueCommand(string command, Identifier identifier, object? data = null)
        {
            var message = new ActionCableOutgoingMessage
            {
                Command = command,
                Identifier = JsonSerializer.Serialize(identifier, identifier.GetType(), JsonSerializerOptions),
                Data = data != null ? JsonSerializer.Serialize(data, JsonSerializerOptions) : null
            };

            return sendTaskRunner.Enqueue(() => SendMessage(message, CancellationToken.None));
        }

        internal async Task Unsubscribe(ActionCableSubscription subscription)
        {
            await EnqueueCommand("unsubscribe", subscription.Identifier);
            subscriptions.Remove(subscription);
        }

        private async Task ReconnectAsync(bool initial = false)
        {
            int reconnectDelayIndex = 0;

            while (webSocket?.IsConnected != true)
            {
                webSocket?.Dispose();
                webSocket = null;

                loopCancellationTokenSource?.Cancel();
                loopCancellationTokenSource = null;

                State = initial ? ClientState.Pending : ClientState.Reconnecting;

                try
                {
                    logger.LogInformation($"Connecting to {Uri}");

                    webSocket = webSocketFactory.CreateWebSocket();
                    webSocket.SetRequestHeader("Origin", Origin);
                    await webSocket.ConnectAsync(Uri, CancellationToken.None).ConfigureAwait(false);

                    logger.LogInformation($"Connected to {Uri}");

                    loopCancellationTokenSource = new CancellationTokenSource();

                    // don't await these since they run until the connection is closed/interrupted
                    _ = Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);

                    foreach (var subscription in subscriptions)
                    {
                        _ = EnqueueCommand("subscribe", subscription.Identifier);
                    }
                }
                catch (WebSocketException)
                {
                    int reconnectDelay = ReconnectDelays[reconnectDelayIndex];
                    logger.LogError($"Failed to connect, waiting {reconnectDelay} ms before retrying...");
                    await Task.Delay(reconnectDelay).ConfigureAwait(false);
                    reconnectDelayIndex = Math.Min(reconnectDelayIndex + 1, ReconnectDelays.Length - 1);
                }
            }
        }

        private async Task SendMessage(ActionCableOutgoingMessage message, CancellationToken cancellationToken)
        {
            if (webSocket == null) throw new InvalidOperationException("WebSocket has not been initialized");

            using var stream = new MemoryStream();

            await JsonSerializer.SerializeAsync(stream, message, JsonSerializerOptions, cancellationToken).ConfigureAwait(false);

            stream.Position = 0;

            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Text, stream.Position >= stream.Length - 1, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop()
        {
            logger.LogInformation($"Started incoming message task");

            try
            {
                while (loopCancellationTokenSource?.IsCancellationRequested == false && webSocket?.IsConnected == true)
                {
                    using var stream = new MemoryStream();
                    var buffer = new byte[BufferSize];
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(buffer, loopCancellationTokenSource.Token).ConfigureAwait(false);
                        stream.Write(new ArraySegment<byte>(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    stream.Position = 0;

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            try
                            {
                                _ = ProcessMessage(stream);
                            }
                            catch (JsonException ex)
                            {
                                logger.LogError("Failed to process message: " + ex);
                            }

                            break;

                        case WebSocketMessageType.Close:
                            logger.LogInformation("Connection closed by remote host");
                            break;

                        default:
                            logger.LogWarning("Unexpected message type " + result.MessageType);
                            break;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (WebSocketException ex)
            {
                await HandleWebSocketException(ex, "Failed to receive message").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                logger.LogError(ex.StackTrace);
            }

            logger.LogInformation($"Incoming message task ended");
        }

        private async Task ProcessMessage(Stream stream)
        {
            ActionCableIncomingMessage? message = await JsonSerializer.DeserializeAsync<ActionCableIncomingMessage>(stream, JsonSerializerOptions);

            if (!message.HasValue) return;

            switch (message.Value.Type)
            {
                case MessageType.Welcome:
                    State = ClientState.Connected;
                    break;

                case MessageType.Disconnect:
                    State = ClientState.Disconnected;
                    break;

                case MessageType.Ping:
                    break;

                case MessageType.Confirmation:
                case MessageType.Rejection:
                case MessageType.None:
                    foreach (var subscription in subscriptions)
                    {
                        subscription.HandleMessage(message.Value);
                    }

                    break;
            }
        }

        private async Task HandleWebSocketException(WebSocketException exception, string message)
        {
            logger.LogError(exception, message);
            loopCancellationTokenSource?.Cancel();
            State = ClientState.Reconnecting;
            await ReconnectAsync().ConfigureAwait(false);
        }
    }
}
