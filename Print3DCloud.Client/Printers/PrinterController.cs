using System;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Interface between an <see cref="IPrinter"/> instance and an <see cref="ActionCableSubscription"/> instance.
    /// </summary>
    internal class PrinterController : IDisposable
    {
        private readonly ILogger<PrinterController> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterController"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        /// <param name="printer">The <see cref="IPrinter"/> to use.</param>
        /// <param name="subscription/">The <see cref="ActionCableSubscription"/> to use.</param>
        public PrinterController(ILogger<PrinterController> logger, IPrinter printer, ActionCableSubscription subscription)
        {
            this.logger = logger;

            this.Printer = printer;
            this.Subscription = subscription;

            this.Subscription.RegisterCallback<SendCommandMessage>("send_command", this.SendCommand);
            this.Subscription.RegisterCallback("reconnect", this.ReconnectPrinter);
        }

        /// <summary>
        /// Gets the <see cref="IPrinter"/> associated with this instance.
        /// </summary>
        public IPrinter Printer { get; }

        /// <summary>
        /// Gets the <see cref="ActionCableSubscription"/> associated with this instance.
        /// </summary>
        public ActionCableSubscription Subscription { get; }

        /// <summary>
        /// Subscribes to the Printer channel with this printer's ID and connects to the printer (if necessary).
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task SubscribeAndConnect(CancellationToken cancellationToken)
        {
            this.Printer.LogMessage += this.Printer_LogMessage;
            this.Printer.StateChanged += this.Printer_StateChanged;

            await this.Subscription.Unsubscribe(cancellationToken); // TODO temporary until some kind of client-level subscribed state caching is implemented
            await this.Subscription.Subscribe(cancellationToken);

            await this.Printer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Printer.LogMessage -= this.Printer_LogMessage;
            this.Printer.StateChanged -= this.Printer_StateChanged;

            this.Printer.Dispose();
            this.Subscription.Dispose();
        }

        private void Printer_LogMessage(string message)
        {
            if (this.Subscription.State != SubscriptionState.Subscribed) return;

            this.Subscription.Perform(new PrinterMessage(message), CancellationToken.None);
        }

        private void Printer_StateChanged(PrinterState state)
        {
            if (this.Subscription.State != SubscriptionState.Subscribed) return;

            this.Subscription.Perform(new PrinterStateMessage(state), CancellationToken.None);
        }

        private async void SendCommand(SendCommandMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Command)) return;

            await this.Printer.SendCommandAsync(message.Command, CancellationToken.None);
        }

        private async void ReconnectPrinter()
        {
            if (this.Printer.State.IsConnected)
            {
                await this.Printer.DisconnectAsync();
            }

            await this.Printer.ConnectAsync(CancellationToken.None);
        }
    }
}
