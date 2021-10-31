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
    /// Interface between a physical machine and the server.
    /// </summary>
    internal class PrinterController : IDisposable
    {
        private readonly IPrinter printer;
        private readonly IActionCableSubscription subscription;
        private readonly ILogger<PrinterController> logger;

        private bool downloading;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterController"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger"/> to use.</param>
        /// <param name="printer">The <see cref="IPrinter"/> being controlled.</param>
        /// <param name="subscription">The <see cref="ActionCableSubscription"/> to use.</param>
        public PrinterController(ILogger<PrinterController> logger, IPrinter printer, IActionCableSubscription subscription)
        {
            this.logger = logger;
            this.printer = printer;
            this.subscription = subscription;

            this.subscription.RegisterCallback<SendCommandMessage>("send_command", this.SendCommand);
            this.subscription.RegisterCallback<AcknowledgeableMessage>("reconnect", this.ReconnectPrinter);
            this.subscription.RegisterCallback<StartPrintMessage>("start_print", this.StartPrint);
            this.subscription.RegisterCallback<AcknowledgeableMessage>("abort_print", this.AbortPrint);
        }

        /// <summary>
        /// Gets the <see cref="PrinterState"/> that represents the current state of this printer.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        public PrinterState State => this.downloading ? PrinterState.Downloading : this.printer.State;

        /// <summary>
        /// Gets the <see cref="PrinterTemperatures"/> containing the latest temperatures reported by the printer.
        /// </summary>
        public virtual PrinterTemperatures? Temperatures => this.printer.Temperatures;

        /// <summary>
        /// Subscribes to the Printer channel with this printer's ID and connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task SubscribeAndConnect(CancellationToken cancellationToken)
        {
            await this.subscription.SubscribeAsync(cancellationToken);
            await this.printer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Unsubscribes from the Printer channel and disconnects from the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task UnsubscribeAndDisconnect(CancellationToken cancellationToken)
        {
            await this.subscription.Unsubscribe(cancellationToken);
            await this.printer.DisconnectAsync(cancellationToken);
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
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.subscription.Dispose();
                this.printer.Dispose();
            }
        }

        private async void SendCommand(SendCommandMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Command)) return;

            await this.printer.SendCommandAsync(message.Command, CancellationToken.None);
        }

        private async void ReconnectPrinter(AcknowledgeableMessage message)
        {
            this.logger.LogInformation("Attempting to reconnect");

            try
            {
                if (this.State != PrinterState.Disconnected)
                {
                    await this.printer.DisconnectAsync(CancellationToken.None);
                }

                await this.printer.ConnectAsync(CancellationToken.None);

                await this.subscription.GuaranteePerformAsync(new AcknowledgeMessage(message.MessageId), CancellationToken.None);
            }
            catch (Exception ex)
            {
                await this.subscription.GuaranteePerformAsync(new AcknowledgeMessage(message.MessageId, ex), CancellationToken.None);
            }
        }

        private async void StartPrint(StartPrintMessage message)
        {
            if (this.State != PrinterState.Ready)
            {
                return;
            }

            this.logger.LogInformation("Starting print {PrintId} from file at '{DownloadUrl}'", message.PrintId, message.DownloadUrl);

            try
            {
                this.downloading = true;

                await this.subscription.GuaranteePerformAsync(
                    new AcknowledgeMessage(message.MessageId),
                    CancellationToken.None);
                await this.subscription.GuaranteePerformAsync(
                    new PrintEventMessage(PrintEventType.Downloading),
                    CancellationToken.None);

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

                this.downloading = false;

                await this.subscription.GuaranteePerformAsync(
                    new PrintEventMessage(PrintEventType.Running),
                    CancellationToken.None);

                await using (FileStream fileStream = new(path, FileMode.Open, FileAccess.Read))
                {
                    try
                    {
                        await this.printer.ExecutePrintAsync(fileStream, CancellationToken.None);
                        await this.subscription.GuaranteePerformAsync(
                            new PrintEventMessage(PrintEventType.Success),
                            CancellationToken.None);
                    }
                    catch (OperationCanceledException)
                    {
                        await this.subscription.GuaranteePerformAsync(
                            new PrintEventMessage(PrintEventType.Canceled),
                            CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError("Print failed\n{Exception}", ex);
                await this.subscription.GuaranteePerformAsync(
                    new PrintEventMessage(PrintEventType.Errored, ex),
                    CancellationToken.None);
            }
            finally
            {
                this.downloading = false;
            }
        }

        private async void AbortPrint(AcknowledgeableMessage message)
        {
            await this.subscription.GuaranteePerformAsync(new AcknowledgeMessage(message.MessageId), CancellationToken.None);

            if (this.printer.State != PrinterState.Printing)
            {
                return;
            }

            try
            {
                await this.printer.AbortPrintAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to abort print\n{Exception}", ex);
            }

            await this.subscription.GuaranteePerformAsync(
                new PrintEventMessage(PrintEventType.Canceled),
                CancellationToken.None);
        }
    }
}
