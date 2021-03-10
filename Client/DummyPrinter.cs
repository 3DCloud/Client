using System;
using System.IO;

namespace Client
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
        public void Connect()
        {
            this.connected = true;
        }

        /// <inheritdoc/>
        public void Disconnect()
        {
            this.connected = false;
        }

        /// <inheritdoc/>
        public void StartPrint(Stream fileStream)
        {
            this.printing = true;
        }

        /// <inheritdoc/>
        public void AbortPrint()
        {
            this.printing = false;
        }

        /// <inheritdoc/>
        public PrinterState GetState()
        {
            return new PrinterState
            {
                IsConnected = this.connected,
                IsPrinting = this.printing,
                HotendTemperatures = new[]
                {
                    210 + this.random.NextDouble() * 0.5,
                    190 + this.random.NextDouble() * 0.5,
                },
                BedTemperature = 60 + this.random.NextDouble() * 0.5,
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.connected = false;
            this.printing = false;
        }
    }
}
