using System;
using System.IO;

namespace Client
{
    internal interface IPrinter : IDisposable
    {
        public string Identifier { get; }

        void Connect();
        void Disconnect();
        void StartPrint(Stream fileStream);
        void AbortPrint();
        PrinterState GetState();
    }
}
