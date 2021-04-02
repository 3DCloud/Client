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
        public string Identifier { get; } = Guid.NewGuid().ToString();

        /// <inheritdoc/>
        public PrinterState State => new PrinterState
        {
            IsConnected = this.connected,
            IsPrinting = this.printing,
            HotendTemperatures = new[]
            {
                new TemperatureSensor { Current = 210 + this.random.NextDouble() * 0.5 },
                new TemperatureSensor { Current = 190 + this.random.NextDouble() * 0.5 },
            },
            BuildPlateTemperature = new TemperatureSensor { Current = 60 + this.random.NextDouble() * 0.5 },
        };

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
