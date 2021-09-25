using System;
using System.IO;
using System.Net.Http;
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
        /// <param name="printer">The <see cref="IPrinter"/> to use.</param>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> to use.</param>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        public PrinterController(IPrinter printer, ActionCableSubscription subscription, ILogger<PrinterController> logger)
        {
            this.Printer = printer;
            this.Subscription = subscription;
            this.logger = logger;

            this.Subscription.RegisterCallback<SendCommandMessage>("send_command", this.SendCommand);
            this.Subscription.RegisterCallback("reconnect", this.ReconnectPrinter);
            this.Subscription.RegisterCallback<StartPrintMessage>("start_print", this.StartPrint);
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

            await this.Subscription.SubscribeAsync(cancellationToken);

            await this.Printer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Printer.LogMessage -= this.Printer_LogMessage;

            this.Printer.Dispose();
            this.Subscription.Dispose();
        }

        private void Printer_LogMessage(string message)
        {
            if (this.Subscription.State != SubscriptionState.Subscribed) return;

            this.Subscription.PerformAsync(new PrinterMessage(message), CancellationToken.None);
        }

        private async void SendCommand(SendCommandMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Command)) return;

            await this.Printer.SendCommandAsync(message.Command, CancellationToken.None);
        }

        private async void ReconnectPrinter()
        {
            if (this.Printer.State != PrinterState.Disconnected)
            {
                await this.Printer.DisconnectAsync();
            }

            await this.Printer.ConnectAsync(CancellationToken.None);
        }

        private async void StartPrint(StartPrintMessage message)
        {
            if (this.Printer.State != PrinterState.Ready)
            {
                return;
            }

            this.logger.LogInformation($"Starting print {message.PrintId} from file at '{message.DownloadUrl}'");

            using HttpClient client = new();
            HttpResponseMessage response = await client.GetAsync(message.DownloadUrl);

            await using Stream fileContentStream = await response.Content.ReadAsStreamAsync();
            await this.Printer.StartPrintAsync(fileContentStream, CancellationToken.None);
        }
    }
}
