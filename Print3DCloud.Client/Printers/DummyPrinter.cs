﻿using System;
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

        private Task? connectedTask;
        private CancellationTokenSource? cancellationTokenSource;

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
        public event Action<string>? LogMessage;

        /// <inheritdoc/>
        public PrinterState State { get; private set; }

        /// <inheritdoc/>
        public PrinterTemperatures Temperatures => new(
            new TemperatureSensor("T0", 210 + this.random.NextDouble() * 0.5, 210),
            new[]
            {
                new TemperatureSensor("T0", 210 + this.random.NextDouble() * 0.5, 210),
                new TemperatureSensor("T1", 190 + this.random.NextDouble() * 0.5, 190),
            },
            new TemperatureSensor("B", 60 + this.random.NextDouble() * 0.5, 60));

        /// <inheritdoc/>
        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Connected");
            this.LogMessage?.Invoke("Connected");
            this.cancellationTokenSource = new CancellationTokenSource();
            this.connectedTask = Task.Run(this.StatusLoop, cancellationToken).ContinueWith(this.HandleStatusLoopTaskCompleted, CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            if (this.connectedTask != null)
            {
                this.cancellationTokenSource?.Cancel();
                await this.connectedTask;
                this.connectedTask = null;
            }

            this.logger.LogInformation("Disconnected");
            this.LogMessage?.Invoke("Disconnected");
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            this.State = PrinterState.Printing;

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task PausePrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Pausing;

            await Task.Delay(3000, cancellationToken);

            this.State = PrinterState.Paused;
        }

        /// <inheritdoc/>
        public async Task ResumePrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Resuming;

            await Task.Delay(3000, cancellationToken);

            this.State = PrinterState.Printing;
        }

        /// <inheritdoc/>
        public async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            this.State = PrinterState.Aborting;

            await Task.Delay(3000, cancellationToken);

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"SEND {command}");
            this.LogMessage?.Invoke($"SEND {command}");
            return Task.Delay(500, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task SendCommandBlockAsync(string commands, CancellationToken cancellationToken)
        {
            foreach (var command in commands.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await this.SendCommandAsync(command.Trim(), cancellationToken);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.State = PrinterState.Disconnected;
        }

        private async Task StatusLoop()
        {
            if (this.cancellationTokenSource == null) return;

            CancellationToken cancellationToken = this.cancellationTokenSource.Token;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                this.LogMessage?.Invoke("RECV temperatures");

                await Task.Delay(1000);
            }
        }

        private void HandleStatusLoopTaskCompleted(Task task)
        {
        }
    }
}
