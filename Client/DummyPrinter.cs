using System;
using System.IO;

namespace Client
{
    internal class DummyPrinter : IPrinter
    {
        public string Identifier { get; } = Guid.NewGuid().ToString();

        private bool connected;
        private bool printing;
        private Random random = new Random();

        public void Connect()
        {
            connected = true;
        }

        public void Disconnect()
        {
            connected = false;
        }

        public void StartPrint(Stream fileStream)
        {
            printing = true;
        }

        public void AbortPrint()
        {
            printing = false;
        }

        public PrinterState GetState()
        {
            return new PrinterState
            {
                IsConnected = connected,
                IsPrinting = printing,
                HotendTemperatures = new[]
                {
                    210 + random.NextDouble() * 0.5,
                    190 + random.NextDouble() * 0.5
                },
                BedTemperature = 60 + random.NextDouble() * 0.5
            };
        }

        public void Dispose()
        {
            connected = false;
            printing = false;
        }
    }
}
