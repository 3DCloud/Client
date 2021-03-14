using System;
using System.Collections.Generic;
using ActionCableSharp;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// A message containing the states of all the printers connected to a client.
    /// </summary>
    internal class PrinterStateMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterStateMessage"/> class.
        /// </summary>
        /// <param name="printerStates">The states of the printers connected to the client.</param>
        public PrinterStateMessage(Dictionary<string, PrinterState> printerStates)
            : base("printer_state")
        {
            this.PrinterStates = printerStates;
            this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Gets the states of the printers connected to the client.
        /// </summary>
        public Dictionary<string, PrinterState> PrinterStates { get; }

        /// <summary>
        /// Gets the time at which the printer states were sampled.
        /// </summary>
        public long Timestamp { get; }
    }
}
