using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Client;
using Moq;
using Xunit;

namespace ActionCableSharp.Tests
{
    public class ActionCableClientTests
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

            // Act
            await client.ConnectAsync();

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Once);
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, CancellationToken.None), Times.Once);
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

            // Act
            await client.ConnectAsync();

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Exactly(2));
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, CancellationToken.None), Times.Exactly(2));
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
            var identifier = new Identifier("channel_name");

            ArraySegment<byte> bytes = JsonSerializer.SerializeToUtf8Bytes(
                new ActionCableOutgoingMessage
                {
                    Command = "subscribe",
                    Identifier = JsonSerializer.Serialize(identifier, client.JsonSerializerOptions),
                    Data = null,
                }, client.JsonSerializerOptions);

            // Act
            await client.ConnectAsync();
            ActionCableSubscription subscription = await client.Subscribe(identifier);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None), Times.Once);
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
            var identifier = new Identifier("channel_name");
            var data = new SampleAction("test");

            ArraySegment<byte> bytes = JsonSerializer.SerializeToUtf8Bytes(
                new ActionCableOutgoingMessage
                {
                    Command = "message",
                    Identifier = JsonSerializer.Serialize(identifier, client.JsonSerializerOptions),
                    Data = JsonSerializer.Serialize(data, client.JsonSerializerOptions),
                }, client.JsonSerializerOptions);

            // Act
            await client.ConnectAsync();
            ActionCableSubscription subscription = await client.Subscribe(identifier);
            await subscription.Perform(data);

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None), Times.Once);
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

            // Act
            await client.ConnectAsync();
            ActionCableSubscription subscription = await client.Subscribe(new Identifier("channel_name"));
            await subscription.Perform(new SampleAction(new string('a', 10_000)));

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, false, CancellationToken.None), Times.Once);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, CancellationToken.None), Times.Exactly(2));
        }
    }
}
