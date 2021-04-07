using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp.Internal;
using Moq;
using Xunit;

namespace ActionCableSharp.Tests
{
    public class ActionCableSubscriptionTests
    {
        [Fact]
        public async Task Subscribe_WithConnectedClient_SendsSubscribeMessage()
        {
            // Arrange
            var mockClient = new Mock<ActionCableClient>(new Uri("ws://example.com"), "dummy");
            mockClient.SetupGet(c => c.State).Returns(ClientState.Connected);

            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            var subscription = new ActionCableSubscription(mockClient.Object, identifier);

            // Act
            await subscription.Subscribe(cancellationToken).ConfigureAwait(false);

            // Assert
            mockClient.Verify(c => c.SendMessageAsync("subscribe", identifier, cancellationToken, null), Times.Once);
            Assert.Equal(SubscriptionState.Pending, subscription.State);
        }

        [Theory]
        [InlineData(ClientState.Connecting)]
        [InlineData(ClientState.Disconnected)]
        [InlineData(ClientState.Disconnecting)]
        [InlineData(ClientState.Reconnecting)]
        [InlineData(ClientState.WaitingForWelcome)]
        public async Task Subscribe_WithDisconnectedClient_DoesNotSendSubscribeMessage(ClientState clientState)
        {
            // Arrange
            var mockClient = new Mock<ActionCableClient>(new Uri("ws://example.com"), "dummy");
            mockClient.SetupGet(c => c.State).Returns(clientState);

            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            var subscription = new ActionCableSubscription(mockClient.Object, identifier);

            // Act
            await subscription.Subscribe(cancellationToken).ConfigureAwait(false);

            // Assert
            mockClient.Verify(c => c.SendMessageAsync("subscribe", identifier, cancellationToken, null), Times.Never);
            Assert.Equal(SubscriptionState.Pending, subscription.State);
        }

        [Fact]
        public async Task MessageReceived_WithConfirmSubscriptionMessage_SetsStateToSubscribed()
        {
            // Arrange
            var mockClient = new Mock<ActionCableClient>(new Uri("ws://example.com"), "dummy");
            mockClient.SetupGet(c => c.State).Returns(ClientState.Connected);

            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            var subscription = new ActionCableSubscription(mockClient.Object, identifier);

            var subscribeMessage = new ActionCableIncomingMessage
            {
                Type = MessageType.ConfirmSubscription,
                Identifier = JsonSerializer.Serialize(identifier, mockClient.Object.JsonSerializerOptions),
            };

            // Act
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            mockClient.Raise(c => c.MessageReceived += null, subscribeMessage);

            // Assert
            mockClient.Verify(c => c.SendMessageAsync("subscribe", identifier, cancellationToken, null), Times.Once);
            Assert.Equal(SubscriptionState.Subscribed, subscription.State);
        }

        [Fact]
        public async Task MessageReceived_WithRejectSubscriptionMessage_SetsStateToRejected()
        {
            // Arrange
            var mockClient = new Mock<ActionCableClient>(new Uri("ws://example.com"), "dummy");
            mockClient.SetupGet(c => c.State).Returns(ClientState.Connected);

            var cancellationToken = new CancellationToken(false);
            var identifier = new Identifier("channel_name");

            var subscription = new ActionCableSubscription(mockClient.Object, identifier);

            var subscribeMessage = new ActionCableIncomingMessage
            {
                Type = MessageType.RejectSubscription,
                Identifier = JsonSerializer.Serialize(identifier, mockClient.Object.JsonSerializerOptions),
            };

            // Act
            await subscription.Subscribe(CancellationToken.None).ConfigureAwait(false);
            mockClient.Raise(c => c.MessageReceived += null, subscribeMessage);

            // Assert
            mockClient.Verify(c => c.SendMessageAsync("subscribe", identifier, cancellationToken, null), Times.Once);
            Assert.Equal(SubscriptionState.Rejected, subscription.State);
        }
    }
}
