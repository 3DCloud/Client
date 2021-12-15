using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// An implementation of <see cref="Printer"/> that gives dummy data.
    /// </summary>
    internal class DummyPrinter : Printer
    {
        private readonly ILogger<DummyPrinter> logger;
        private readonly Random random;

        private Task? printTask;
        private CancellationTokenSource? printCancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="DummyPrinter"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="subscription">The subscription to use when communicating with the server.</param>
        public DummyPrinter(ILogger<DummyPrinter> logger, IActionCableSubscription subscription)
            : base(logger, subscription)
        {
            this.logger = logger;
            this.random = new Random();
        }

        /// <inheritdoc/>
        public override PrinterTemperatures Temperatures => new(
            new List<TemperatureSensor>
            {
                new("T0", 210 + this.random.NextDouble() - 0.5, 210),
                new("T1", 190 + this.random.NextDouble() - 0.5, 190),
            },
            new TemperatureSensor("B", 60 + this.random.NextDouble() - 0.5, 60));

        /// <inheritdoc/>
        public override Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Connected");
            this.State = PrinterState.Ready;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            this.printCancellationTokenSource?.Cancel();
            this.printCancellationTokenSource = null;

            if (this.printTask != null)
            {
                await this.printTask;
                this.printTask = null;
            }

            this.State = PrinterState.Disconnected;
            this.logger.LogInformation("Disconnected");
        }

        /// <inheritdoc/>
        public override async Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            this.State = PrinterState.Downloading;
            await this.SendPrintEvent(PrintEventType.Downloading, CancellationToken.None);

            await Task.Delay(2_000, cancellationToken);

            this.printCancellationTokenSource = new CancellationTokenSource();
            this.logger.LogInformation("Starting print");
            this.State = PrinterState.Printing;
            await this.SendPrintEvent(PrintEventType.Running, CancellationToken.None);

            this.printTask = Task.Run(
                async () =>
                {
                    await Task.Delay(5_000, this.printCancellationTokenSource.Token);
                    await this.SendPrintEvent(PrintEventType.Success, CancellationToken.None);
                    this.State = PrinterState.Ready;
                    this.logger.LogInformation("Print completed");
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public override async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Canceling;

            await Task.Delay(1000, cancellationToken);

            await this.SendPrintEvent(PrintEventType.Canceled, CancellationToken.None);
            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public override Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            return Task.Delay(50, cancellationToken);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.State = PrinterState.Disconnected;
        }
    }
}
