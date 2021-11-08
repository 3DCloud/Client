using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// An implementation of <see cref="IPrinter"/> that gives dummy data.
    /// </summary>
    internal class DummyPrinter : IPrinter
    {
        private readonly ILogger<DummyPrinter> logger;
        private readonly Random random;

        private Task? printTask;
        private CancellationTokenSource? printCancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="DummyPrinter"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        public DummyPrinter(ILogger<DummyPrinter> logger)
        {
            this.logger = logger;
            this.random = new Random();
        }

        /// <inheritdoc/>
        public PrinterState State { get; private set; }

        /// <inheritdoc/>
        public PrinterTemperatures Temperatures => new(
            new List<TemperatureSensor>
            {
                new("T0", 210 + this.random.NextDouble() - 0.5, 210),
                new("T1", 190 + this.random.NextDouble() - 0.5, 190),
            },
            new TemperatureSensor("B", 60 + this.random.NextDouble() - 0.5, 60));

        public int? TimeRemaining { get; }

        public double? Progress { get; }

        /// <inheritdoc/>
        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Connected");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            this.printCancellationTokenSource?.Cancel();
            this.printCancellationTokenSource = null;

            if (this.printTask != null)
            {
                await this.printTask;
                this.printTask = null;
            }

            this.logger.LogInformation("Disconnected");
        }

        /// <inheritdoc/>
        public Task ExecutePrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            this.State = PrinterState.Printing;

            this.printCancellationTokenSource = new CancellationTokenSource();

            return Task.Delay(30_000, this.printCancellationTokenSource.Token);
        }

        /// <inheritdoc/>
        public Task PausePrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Pausing;

            this.printCancellationTokenSource?.Cancel();

            this.State = PrinterState.Paused;

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ResumePrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Resuming;

            this.State = PrinterState.Printing;

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Canceling;

            await Task.Delay(3000, cancellationToken);

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            return Task.Delay(50, cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="DummyPrinter"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected void Dispose(bool disposing)
        {
            this.State = PrinterState.Disconnected;
        }
    }
}
