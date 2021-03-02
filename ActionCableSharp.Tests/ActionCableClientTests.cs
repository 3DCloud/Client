using ActionCableSharp.Internal;
using Client;
using Moq;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ActionCableSharp.Tests
{
    public class ActionCableClientTests
    {
        [Fact]
        public async Task Connect_HostAvailableImmediately_ConnectsSuccessfully()
        {
            // Arrange
            Uri uri = new Uri("ws://example.com");
            bool connected = false;

            Mock<IWebSocket> mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Callback(() => connected = true).Returns(Task.CompletedTask);
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            Mock<IWebSocketFactory> mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            ActionCableClient client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);

            // Act
            await client.ConnectAsync();

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Once);
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, CancellationToken.None), Times.Once);
        }

        [Fact]
        public async Task Connect_HostAvailableAfterRetry_ConnectsSuccessfully()
        {
            // Arrange
            Uri uri = new Uri("ws://example.com");
            bool connected = false;

            Mock<IWebSocket> mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.SetupSequence(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new WebSocketException())
                .Returns(() => { connected = true; return Task.CompletedTask; });
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(() => connected);

            Mock<IWebSocketFactory> mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            ActionCableClient client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);

            // Act
            await client.ConnectAsync();

            // Assert
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"), Times.Exactly(2));
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, CancellationToken.None), Times.Exactly(2));
        }

        [Fact]
        public async Task EnqueueMessage_Subscribe_ReturnsValidSubscription()
        {
            // Arrange
            Uri uri = new Uri("ws://example.com");

            Mock<IWebSocket> mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.SetupGet(ws => ws.IsConnected).Returns(true);

            Mock<IWebSocketFactory> mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            ActionCableClient client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);

            // Act
            await client.ConnectAsync();
            ActionCableSubscription subscription = await client.Subscribe(new Identifier("channel_name"));

            // Assert
            Assert.Equal("channel_name", subscription.Identifier.ChannelName);
            mockWebSocket.Verify(ws => ws.SendAsync(It.IsAny<ArraySegment<byte>>(), WebSocketMessageType.Text, true, CancellationToken.None), Times.Once);
        }
    }
}
