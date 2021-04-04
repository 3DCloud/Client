using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// An implementation of <see cref="IPrinter"/> that gives dummy data.
    /// </summary>
    internal class DummyPrinter : IPrinter
    {
        private readonly Random random = new Random();

        private bool connected;
        private bool printing;

        /// <inheritdoc/>
        public PrinterState State => new PrinterState(
            this.connected,
            this.printing,
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
            this.connected = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DisconnectAsync()
        {
            this.connected = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            this.printing = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.connected = false;
            this.printing = false;
        }
    }
}
