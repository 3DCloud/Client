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
        private const int ReconnectDelay = 2000;
        private const int BufferSize = 8192;

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

        private ILogger<ActionCableClient> logger;
        private ClientWebSocket? webSocket;
        private CancellationTokenSource? loopCancellationTokenSource;
        private Thread? sendThread;
        private Thread? receiveThread;

        private readonly List<ActionCableSubscription> subscriptions;
        private readonly ConcurrentQueue<ActionCableOutgoingMessage> sendQueue;

        /// <summary>
        /// Creates a new instance of the <see cref="ActionCableClient"/> class.
        /// </summary>
        /// <param name="uri">URI pointing to an Action Cable mount path.</param>
        /// <param name="origin">Origin to use in the headers of requests.</param>
        public ActionCableClient(Uri uri, string origin)
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
            subscriptions = new List<ActionCableSubscription>();
            sendQueue = new ConcurrentQueue<ActionCableOutgoingMessage>();
            loopCancellationTokenSource = new CancellationTokenSource();
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
            if (webSocket?.State != WebSocketState.Open) return;

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

            EnqueueCommand("subscribe", identifier);

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
            logger.LogInformation($"Connecting to {Uri}");

            while (webSocket?.State != WebSocketState.Open)
            {
                loopCancellationTokenSource?.Cancel();

                State = initial ? ClientState.Pending : ClientState.Reconnecting;

                try
                {
                    webSocket = new ClientWebSocket();
                    webSocket.Options.SetRequestHeader("Origin", Origin);
                    await webSocket.ConnectAsync(Uri, CancellationToken.None);

                    logger.LogInformation($"Connected to {Uri}");

                    sendThread = new Thread(SendLoop);
                    receiveThread = new Thread(ReceiveLoop);
                    loopCancellationTokenSource = new CancellationTokenSource();

                    sendThread.Start();
                    receiveThread.Start();

                    foreach (var subscription in subscriptions)
                    {
                        EnqueueCommand("subscribe", subscription.Identifier);
                    }
                }
                catch (WebSocketException)
                {
                    logger.LogError($"Failed to connect, waiting {ReconnectDelay} ms before retrying...");
                    await Task.Delay(ReconnectDelay);
                }
            }
        }

        private async void SendLoop()
        {
            logger.LogInformation($"Started outgoing message thread");

            try
            {
                while (loopCancellationTokenSource?.IsCancellationRequested == false && webSocket?.State == WebSocketState.Open)
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
            catch (WebSocketException)
            {
                await HandleWebSocketException();
            }

            logger.LogInformation($"Stopped outgoing message thread");
        }

        private async void ReceiveLoop()
        {
            logger.LogInformation($"Started incoming message thread");

            try
            {
                var builder = new StringBuilder(BufferSize);

                while (loopCancellationTokenSource?.IsCancellationRequested == false && webSocket?.State == WebSocketState.Open)
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

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        try
                        {
                            ProcessMessage(message);
                        }
                        catch (JsonException ex)
                        {
                            logger.LogError("Failed to process message: " + ex);
                        }
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (WebSocketException)
            {
                await HandleWebSocketException();
            }

            logger.LogInformation($"Stopped incoming message thread");
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

        private async Task HandleWebSocketException()
        {
            if (webSocket?.State == WebSocketState.Aborted)
            {
                loopCancellationTokenSource?.Cancel();
                State = ClientState.Reconnecting;
                await ReconnectAsync();
            }
        }
    }
}
