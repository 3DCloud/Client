using ActionCableSharp;
using System;
using System.Collections.Generic;

namespace Client
{
    internal class PrinterStateMessage : ActionMessage
    {
        public Dictionary<string, PrinterState> PrinterStates { get; }
        public long Timestamp { get; }

        public PrinterStateMessage(Dictionary<string, PrinterState> printerStates) : base("printer_state")
        {
            PrinterStates = printerStates;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
