using ActionCableSharp.Internal;
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
        public void Connect_HostAvailableImmediately_ConnectsSuccessfully()
        {
            // Arrange
            AutoResetEvent resetEvent = new AutoResetEvent(false);
            Uri uri = new Uri("ws://example.com");

            Mock<IWebSocket> mockWebSocket = new Mock<IWebSocket>();
            mockWebSocket.Setup(ws => ws.SetRequestHeader(It.IsAny<string>(), It.IsAny<string?>()));
            mockWebSocket.Setup(ws => ws.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Callback(() => resetEvent.Set());

            Mock<IWebSocketFactory> mockWebSocketFactory = new Mock<IWebSocketFactory>();
            mockWebSocketFactory.Setup(f => f.CreateWebSocket()).Returns(mockWebSocket.Object);

            ActionCableClient client = new ActionCableClient(uri, "dummy", mockWebSocketFactory.Object);

            // Act
            client.Connect();

            // Assert
            Assert.True(resetEvent.WaitOne(1000), "Timed out");
            mockWebSocket.Verify(ws => ws.SetRequestHeader("Origin", "dummy"));
            mockWebSocket.Verify(ws => ws.ConnectAsync(uri, CancellationToken.None));
        }
    }
}
