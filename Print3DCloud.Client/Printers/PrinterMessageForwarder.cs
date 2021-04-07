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
    internal class PrinterMessageForwarder : IMessageReceiver
    {
        private readonly ILogger<PrinterMessageForwarder> logger;

        private ActionCableSubscription? subscription;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterMessageForwarder"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        /// <param name="printer">The <see cref="IPrinter"/> to use.</param>
        public PrinterMessageForwarder(ILogger<PrinterMessageForwarder> logger, IPrinter printer)
        {
            this.Printer = printer;
            this.logger = logger;
        }

        /// <summary>
        /// Gets the <see cref="IPrinter"/> associated with this instance.
        /// </summary>
        public IPrinter Printer { get; }

        /// <inheritdoc/>
        public async void Subscribed(ActionCableSubscription subscription)
        {
            this.subscription = subscription;

            this.Printer.LogMessage += this.Printer_LogMessage;
            this.Printer.StateChanged += this.Printer_StateChanged;

            if (!this.Printer.State.IsConnected)
            {
                try
                {
                    await this.Printer.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    try
                    {
                        await this.subscription.Unsubscribe(CancellationToken.None);
                    }
                    finally
                    {
                        this.subscription.Dispose();
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Rejected()
        {
        }

        /// <inheritdoc/>
        public void Unsubscribed()
        {
            this.Printer.LogMessage -= this.Printer_LogMessage;
            this.Printer.StateChanged -= this.Printer_StateChanged;
        }

        [ActionMethod]
        private Task SendCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return Task.CompletedTask;

            return this.Printer.SendCommandAsync(command, CancellationToken.None);
        }

        private void Printer_LogMessage(string message)
        {
            if (this.subscription?.State != SubscriptionState.Subscribed) return;

            this.subscription.Perform(new PrinterMessage(message), CancellationToken.None);
        }

        private void Printer_StateChanged(PrinterState state)
        {
            if (this.subscription?.State != SubscriptionState.Subscribed) return;

            this.subscription.Perform(new PrinterStateMessage(state), CancellationToken.None);
        }
    }
}
