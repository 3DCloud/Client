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
    internal abstract class Printer : IDisposable
    {
        private const int ConnectTimeOutDelayMs = 10_000;

        private readonly ILogger logger;
        private readonly IActionCableSubscription subscription;
        private readonly HttpClient httpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="Printer"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="subscription">The subscription to use when communicating with the server.</param>
        protected Printer(ILogger logger, IActionCableSubscription subscription)
        {
            this.logger = logger;
            this.subscription = subscription;

            this.httpClient = new HttpClient();

            this.subscription.RegisterAcknowledgeableCallback<AcknowledgeableMessage>("reconnect", this.HandleReconnectPrinterMessage);
            this.subscription.RegisterAcknowledgeableCallback<StartPrintMessage>("start_print", this.HandleStartPrintMessage);
            this.subscription.RegisterAcknowledgeableCallback<AcknowledgeableMessage>("abort_print", this.HandleAbortPrintMessage);
        }

        /// <summary>
        /// Gets or sets the <see cref="PrinterState"/> that represents the current state of this printer.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        public PrinterState State { get; protected set; }

        /// <summary>
        /// Gets or sets the <see cref="PrinterTemperatures"/> containing the latest temperatures reported by the printer.
        /// </summary>
        public virtual PrinterTemperatures? Temperatures { get; protected set; }

        /// <summary>
        /// Gets or sets the estimated amount of time remaining for the ongoing print.
        /// </summary>
        public int? TimeRemaining { get; protected set; }

        /// <summary>
        /// Gets or sets the estimated percentage completion of the ongoing print.
        /// </summary>
        public double? Progress { get; protected set; }

        /// <summary>
        /// Subscribes to the Printer channel with this printer's ID and connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task SubscribeAndConnect(CancellationToken cancellationToken)
        {
            await this.subscription.SubscribeAsync(cancellationToken);
            await this.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Unsubscribes from the Printer channel and disconnects from the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the subscription request has been sent and the printer is connected.</returns>
        public async Task UnsubscribeAndDisconnect(CancellationToken cancellationToken)
        {
            await this.subscription.Unsubscribe(cancellationToken);
            await this.DisconnectAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.subscription.Dispose();
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public abstract Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public abstract Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Runs a print on this printer.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing the file to print.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been started.</returns>
        public abstract Task StartPrintAsync(Stream stream, CancellationToken cancellationToken);

        /// <summary>
        /// Aborts the print that is currently running.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been aborted.</returns>
        public abstract Task AbortPrintAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sends a command to the printer.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        public abstract Task SendCommandAsync(string command, CancellationToken cancellationToken);

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Printer"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// Checks whether the printer is in the specified state. Includes sub-states.
        /// </summary>
        /// <param name="printerState">The printer state to compare against.</param>
        /// <returns>Whether the printer is in the specified state or not.</returns>
        protected bool IsInState(PrinterState printerState)
        {
            switch (printerState)
            {
                // states with no sub-states
                case PrinterState.Disconnected:
                case PrinterState.Connecting:
                case PrinterState.Disconnecting:
                case PrinterState.Downloading:
                case PrinterState.Heating:
                case PrinterState.Canceling:
                case PrinterState.Ready:
                    return this.State == printerState;

                case PrinterState.Connected:
                    return this.State is
                        PrinterState.Ready or
                        PrinterState.Busy or
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating or
                        PrinterState.Canceling;

                case PrinterState.Busy:
                    return this.State is
                        PrinterState.Busy or
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating or
                        PrinterState.Canceling;

                case PrinterState.Printing:
                    return this.State is
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating;
            }

            return false;
        }

        /// <summary>
        /// Send a print event to the server.
        /// </summary>
        /// <param name="eventType">The type of event.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the event has been successfully sent.</returns>
        protected Task SendPrintEvent(PrintEventType eventType, CancellationToken cancellationToken)
        {
            return this.SendPrintEvent(eventType, null, cancellationToken);
        }

        /// <summary>
        /// Send a print event to the server.
        /// </summary>
        /// <param name="eventType">The type of event.</param>
        /// <param name="exception">The exception associated with the event.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the event has been successfully sent.</returns>
        protected Task SendPrintEvent(PrintEventType eventType, Exception? exception, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Sending {EventType} print event", eventType);
            return this.subscription.GuaranteePerformAsync(new PrintEventMessage(eventType, exception), cancellationToken);
        }

        private async void HandleReconnectPrinterMessage(AcknowledgeableMessage message, AcknowledgeCallback ack)
        {
            this.logger.LogInformation("Attempting to reconnect");

            try
            {
                if (this.State != PrinterState.Disconnected)
                {
                    await this.DisconnectAsync(CancellationToken.None);
                }

                await this.ConnectAsync(new CancellationTokenSource(ConnectTimeOutDelayMs).Token);

                ack();
            }
            catch (Exception ex)
            {
                ack(ex);
            }
        }

        private async void HandleStartPrintMessage(StartPrintMessage message, AcknowledgeCallback ack)
        {
            if (this.State != PrinterState.Ready)
            {
                ack(new Exception("Printer isn't ready"));
            }

            this.logger.LogInformation("Starting print {PrintId} from file at '{DownloadUrl}'", message.PrintId, message.DownloadUrl);

            ack();

            try
            {
                this.State = PrinterState.Downloading;

                await this.SendPrintEvent(PrintEventType.Downloading, CancellationToken.None);

                using HttpResponseMessage response = await this.httpClient.GetAsync(message.DownloadUrl);
                await using Stream contentStream = await response.Content.ReadAsStreamAsync();
                await this.StartPrintAsync(contentStream, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError("Print failed\n{Exception}", ex);
                await this.SendPrintEvent(PrintEventType.Errored, ex, CancellationToken.None);
            }
        }

        private async void HandleAbortPrintMessage(AcknowledgeableMessage message, AcknowledgeCallback ack)
        {
            this.logger.LogInformation("Received abort request");

            if (!this.IsInState(PrinterState.Printing))
            {
                ack(new Exception("Printer is not printing"));
            }

            ack();

            try
            {
                await this.AbortPrintAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to abort print\n{Exception}", ex);
            }
        }
    }
}
