using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Moq;
using Xunit;

namespace ActionCableSharp.Tests
{
    public class ActionCableClientTest
    {
        [Fact]
        public async Task ConnectAsync_HostAvailableImmediately_ConnectsSuccessfully()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var connected = false;

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Callback(() => connected = true).Returns(Task.CompletedTask);
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Once);
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task ConnectAsync_HostAvailableAfterRetry_ConnectsSuccessfully()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var connected = false;

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.SetupSequence(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new WebSocketException())
                .Returns(() =>
                {
                    connected = true;
                    return Task.CompletedTask;
                });
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Exactly(2));
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, cancellationToken), Times.Exactly(2));
        }

        [Fact]
        public async Task Subscribe_WithValidIdentifier_ReturnsValidSubscription()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            ArraySegment<byte> bytes = JsonSerializer.SerializeToUtf8Bytes(
                new ActionCableOutgoingMessage
                {
                    Command = "subscribe",
                    Identifier = JsonSerializer.Serialize(identifier, client.JsonSerializerOptions),
                    Data = null,
                },
                client.JsonSerializerOptions);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ActionCableSubscription subscription = client.CreateSubscription(identifier);
            await subscription.Subscribe(CancellationToken.None);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task Subscribe_AfterReconnect_Resubscribes()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var connected = false;

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => connected = true);
            mockWebSocket.Setup(ws => ws.CloseAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => connected = false);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var identifier = new Identifier("channel_name");

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ActionCableSubscription subscription = client.CreateSubscription(identifier);
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, CancellationToken.None), Times.Exactly(2));
        }

        [Fact]
        public async Task Subscribe_WhenDisconnected_ThrowsException()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var identifier = new Identifier("channel_name");
            ActionCableSubscription subscription = client.CreateSubscription(identifier);

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => subscription.Subscribe(CancellationToken.None)).ConfigureAwait(false);
        }

        [Fact]
        public async Task Unsubscribe_WithSubscribedSubscription_SendsUnsubscribe()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            ArraySegment<byte> bytes = JsonSerializer.SerializeToUtf8Bytes(
                new ActionCableOutgoingMessage
                {
                    Command = "unsubscribe",
                    Identifier = JsonSerializer.Serialize(identifier, client.JsonSerializerOptions),
                },
                client.JsonSerializerOptions);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ActionCableSubscription subscription = client.CreateSubscription(identifier);
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            await subscription.Unsubscribe(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task Perform_WithValidMessage_SendsMessage()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");
            var data = new SampleMessage("test");

            ArraySegment<byte> bytes = JsonSerializer.SerializeToUtf8Bytes(
                new ActionCableOutgoingMessage
                {
                    Command = "message",
                    Identifier = JsonSerializer.Serialize(identifier, client.JsonSerializerOptions),
                    Data = JsonSerializer.Serialize(data, client.JsonSerializerOptions),
                },
                client.JsonSerializerOptions);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ActionCableSubscription subscription = client.CreateSubscription(identifier);
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            await subscription.Perform(data, cancellationToken).ConfigureAwait(false);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken), Times.Once);
        }

        [Fact]
        public async Task Perform_WithLargePayload_SendsMessageInChunks()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            ActionCableSubscription subscription = client.CreateSubscription(new Identifier("channel_name"));
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            await subscription.Perform(new SampleMessage(new string('a', 10_000)), cancellationToken).ConfigureAwait(false);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, false, CancellationToken.None), Times.Once);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, CancellationToken.None), Times.Exactly(2));
        }

        [Fact]
        public async Task DisconnectAsync_WhenConnected_ClosesWebSocket()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var connected = false;

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Callback(() => connected = true).Returns(Task.CompletedTask);
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);

            // Act
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken), Times.Once);
            Assert.Equal(ClientState.Disconnected, client.State);
        }

        [Fact]
        public async Task DisconnectAsync_NeverConnected_DoesNothing()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);

            // Assert
            Assert.Equal(ClientState.Disconnected, client.State);
        }

        [Fact]
        public async Task ReceiveMessage_WhenWelcomeReceived_SetsClientState()
        {
            // Arrange
            var uri = new Uri("ws://example.com");
            var connected = false;

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Callback(() => connected = true).Returns(Task.CompletedTask);
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            ArraySegment<byte> receivedMessage = JsonSerializer.SerializeToUtf8Bytes(new { type = "welcome" });
            mockWebSocket
                .Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
                .Callback((ArraySegment<byte> bytes, CancellationToken cancellationToken) => receivedMessage.CopyTo(bytes))
                .ReturnsAsync(new WebSocketReceiveResult(receivedMessage.Count, WebSocketMessageType.Text, true));

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ReceiveMessage(cancellationToken).ConfigureAwait(false);

            // Assert
            Assert.Equal(ClientState.Connected, client.State);
        }

        [Fact]
        public async Task ReceiveMessage_WhenUnexpectedMessageTypeReceived_IgnoresMessage()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);
            mockWebSocket.Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WebSocketReceiveResult(0, WebSocketMessageType.Binary, true));

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ReceiveMessage(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), cancellationToken), Times.Once);
        }

        [Fact]
        public async Task ReceiveMessage_NullContent_IgnoresMessage()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            mockWebSocket
                .Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ReceiveMessage(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), cancellationToken), Times.Once);
        }

        [Fact]
        public async Task ReceiveMessage_DisconnectMessage_SetsClientState()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            ArraySegment<byte> receivedMessage = JsonSerializer.SerializeToUtf8Bytes(new { type = "disconnect", reason = (string?)null, reconnect = false });
            mockWebSocket
                .Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
                .Callback((ArraySegment<byte> bytes, CancellationToken cancellationToken) => receivedMessage.CopyTo(bytes))
                .ReturnsAsync(new WebSocketReceiveResult(receivedMessage.Count, WebSocketMessageType.Text, true));

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ReceiveMessage(cancellationToken).ConfigureAwait(false);

            // Assert
            Assert.Equal(ClientState.Disconnecting, client.State);
        }

        [Fact]
        public async Task ReceiveMessage_CloseMessage_ClosesWebSocketOutput()
        {
            // Arrange
            var uri = new Uri("ws://example.com");

            var mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            mockWebSocket
                .Setup(ws => ws.ReceiveAsync(It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

            var mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            var client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);
            var cancellationToken = new CancellationToken(false);

            // Act
            await client.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            await client.ReceiveMessage(cancellationToken).ConfigureAwait(false);

            // Assert
            mockWebSocket.Verify(ws => ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed by request from server", cancellationToken), Times.Once);
        }
    }
}
