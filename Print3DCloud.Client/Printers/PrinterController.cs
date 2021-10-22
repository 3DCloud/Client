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

        private long? currentPrintId;

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
            this.Subscription.RegisterCallback<AcknowledgeableMessage>("reconnect", this.ReconnectPrinter);
            this.Subscription.RegisterCallback<StartPrintMessage>("start_print", this.StartPrint);
            this.Subscription.RegisterCallback<AcknowledgeableMessage>("abort_print", this.AbortPrint);
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
        /// Subscribes to the Printer channel with this printer's ID and connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task SubscribeAndConnect(CancellationToken cancellationToken)
        {
            await this.Subscription.SubscribeAsync(cancellationToken);
            await this.Printer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Unsubscribes from the Printer channel and disconnects from the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task UnsubscribeAndDisconnect(CancellationToken cancellationToken)
        {
            await this.Subscription.Unsubscribe(cancellationToken);
            await this.Printer.DisconnectAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the System.IO.Ports.SerialPort and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Printer.Dispose();
                this.Subscription.Dispose();
            }
        }

        private async void SendCommand(SendCommandMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Command)) return;

            await this.Printer.SendCommandAsync(message.Command, CancellationToken.None);
        }

        private async void ReconnectPrinter(AcknowledgeableMessage message)
        {
            this.logger.LogInformation("Attempting to reconnect");

            try
            {
                if (this.Printer.State != PrinterState.Disconnected)
                {
                    await this.Printer.DisconnectAsync(CancellationToken.None);
                }

                await this.Printer.ConnectAsync(CancellationToken.None);

                await this.Subscription.PerformAsync(new AcknowledgeMessage(message.MessageId), CancellationToken.None);
            }
            catch (Exception ex)
            {
                await this.Subscription.PerformAsync(new AcknowledgeMessage(message.MessageId, ex), CancellationToken.None);
            }
        }

        private async void StartPrint(StartPrintMessage message)
        {
            if (this.Printer.State != PrinterState.Ready)
            {
                return;
            }

            this.logger.LogInformation($"Starting print {message.PrintId} from file at '{message.DownloadUrl}'");

            try
            {
                await this.Subscription.PerformAsync(new AcknowledgeMessage(message.MessageId), CancellationToken.None);
                await this.Subscription.PerformAsync(new PrintEventMessage(message.PrintId, PrintEventType.Downloading), CancellationToken.None);

                string directory = Path.Join(Directory.GetCurrentDirectory(), "tmp");
                Directory.CreateDirectory(directory);

                string path = Path.Join(directory, Guid.NewGuid().ToString());

                using (HttpClient client = new())
                {
                    HttpResponseMessage response = await client.GetAsync(message.DownloadUrl);

                    // save to filesystem to reduce memory usage
                    await using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    await using (FileStream writeFileStream = new(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        await contentStream.CopyToAsync(writeFileStream);
                    }
                }

                // we don't use "using" here since the file needs to be kept open for the duration of the print
                FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
                await this.Printer.StartPrintAsync(fileStream, CancellationToken.None).ConfigureAwait(false);

                await this.Subscription.PerformAsync(new PrintEventMessage(message.PrintId, PrintEventType.Running), CancellationToken.None);

                this.currentPrintId = message.PrintId;
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to start print");
                this.logger.LogError(ex.ToString());

                await this.Subscription.PerformAsync(new PrintEventMessage(message.PrintId, PrintEventType.Errored, ex), CancellationToken.None);
            }
        }

        private async void AbortPrint(AcknowledgeableMessage message)
        {
            try
            {
                if (this.currentPrintId.HasValue && this.Printer.State == PrinterState.Printing)
                {
                    await this.Printer.AbortPrintAsync(CancellationToken.None);
                    await this.Subscription.PerformAsync(
                        new PrintEventMessage(this.currentPrintId.Value, PrintEventType.Canceled),
                        CancellationToken.None);

                    this.currentPrintId = null;
                }

                await this.Subscription.PerformAsync(new AcknowledgeMessage(message.MessageId), CancellationToken.None);
            }
            catch (Exception ex)
            {
                await this.Subscription.PerformAsync(new AcknowledgeMessage(message.MessageId, ex), CancellationToken.None);
            }
        }
    }
}
