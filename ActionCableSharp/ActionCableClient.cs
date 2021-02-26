using ActionCableSharp.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
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
        private readonly ConcurrentQueue<ActionCableOutgoingMessage> sendQueue;

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
            sendQueue = new ConcurrentQueue<ActionCableOutgoingMessage>();
        }

        /// <summary>
        /// Initiates the WebSocket connection.
        /// </summary>
        public void Connect()
        {
            Task.Run(() => ReconnectAsync(true));
        }

        public async Task DisconnectAsync()
        {
            if (webSocket?.IsConnected != true) return;

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);

            State = ClientState.Disconnected;
        }

        /// <summary>
        /// Subscribes to a specific Action Cable channel.
        /// </summary>
        /// <param name="identifier">Identifier for the channel.</param>
        /// <returns>A reference to the <see cref="ActionCableSubscription"/> linked to the specified <paramref name="identifier"/>.</returns>
        public ActionCableSubscription Subscribe(Identifier identifier)
        {
            var subscription = new ActionCableSubscription(this, identifier);
            subscriptions.Add(subscription);

            if (webSocket?.IsConnected == true)
            {
                EnqueueCommand("subscribe", identifier);
            }

            return subscription;
        }

        public void Dispose()
        {
            webSocket?.Dispose();
        }

        internal void EnqueueCommand(string command, Identifier identifier, object? data = null)
        {
            var message = new ActionCableOutgoingMessage
            {
                Command = command,
                Identifier = JsonSerializer.Serialize(identifier, identifier.GetType(), JsonSerializerOptions),
                Data = data != null ? JsonSerializer.Serialize(data, JsonSerializerOptions) : null
            };

            sendQueue.Enqueue(message);
        }

        internal void Unsubscribe(ActionCableSubscription subscription)
        {
            EnqueueCommand("unsubscribe", subscription.Identifier);
            subscriptions.Remove(subscription);
        }

        private async Task ReconnectAsync(bool initial = true)
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
                    await webSocket.ConnectAsync(Uri, CancellationToken.None);

                    logger.LogInformation($"Connected to {Uri}");

                    loopCancellationTokenSource = new CancellationTokenSource();

                    // don't await these since they run until the connection is closed/interrupted
                    _ = Task.Factory.StartNew(SendLoop, TaskCreationOptions.LongRunning).ConfigureAwait(false);
                    _ = Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning).ConfigureAwait(false);

                    foreach (var subscription in subscriptions)
                    {
                        EnqueueCommand("subscribe", subscription.Identifier);
                    }
                }
                catch (WebSocketException)
                {
                    int reconnectDelay = ReconnectDelays[reconnectDelayIndex];
                    logger.LogError($"Failed to connect, waiting {reconnectDelay} ms before retrying...");
                    await Task.Delay(reconnectDelay);
                    reconnectDelayIndex = Math.Min(reconnectDelayIndex + 1, ReconnectDelays.Length - 1);
                }
            }
        }

        private async Task SendLoop()
        {
            logger.LogInformation($"Started outgoing message task");

            try
            {
                while (loopCancellationTokenSource?.IsCancellationRequested == false && webSocket?.IsConnected == true)
                {
                    if (!sendQueue.TryDequeue(out ActionCableOutgoingMessage message)) continue;

                    using var stream = new MemoryStream();

                    await JsonSerializer.SerializeAsync(stream, message, JsonSerializerOptions);

                    stream.Position = 0;

                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;

                    while ((bytesRead = stream.Read(buffer)) > 0)
                    {
                        await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, bytesRead), WebSocketMessageType.Text, stream.Position >= stream.Length - 1, loopCancellationTokenSource.Token);
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (WebSocketException ex)
            {
                await HandleWebSocketException(ex, "Failed to send message");
            }

            logger.LogInformation($"Outgoing message task ended");
        }

        private async Task ReceiveLoop()
        {
            logger.LogInformation($"Started incoming message task");

            try
            {
                var builder = new StringBuilder(BufferSize);

                while (loopCancellationTokenSource?.IsCancellationRequested == false && webSocket?.IsConnected == true)
                {
                    builder.Clear();
                    var buffer = new byte[BufferSize];
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(buffer, loopCancellationTokenSource.Token);
                        builder.Append(Encoding.UTF8.GetString(new ArraySegment<byte>(buffer, 0, result.Count)));
                    }
                    while (!result.EndOfMessage);

                    string message = builder.ToString();

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            try
                            {
                                ProcessMessage(message);
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
                await HandleWebSocketException(ex, "Failed to receive message");
            }

            logger.LogInformation($"Incoming message task ended");
        }

        private void ProcessMessage(string messageString)
        {
            ActionCableIncomingMessage? message = JsonSerializer.Deserialize<ActionCableIncomingMessage>(messageString, JsonSerializerOptions);

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
                        Task.Run(() => subscription.HandleMessage(message.Value));
                    }
                    break;
            }
        }

        private async Task HandleWebSocketException(WebSocketException exception, string message)
        {
            logger.LogError(exception, message);
            loopCancellationTokenSource?.Cancel();
            State = ClientState.Reconnecting;
            await ReconnectAsync();
        }
    }
}
