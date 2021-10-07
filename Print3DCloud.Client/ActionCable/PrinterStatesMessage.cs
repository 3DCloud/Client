using System;
using System.Collections.Generic;
using ActionCableSharp;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// A message containing the state of a specific printer.
    /// </summary>
    internal class PrinterStatesMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterStatesMessage"/> class.
        /// </summary>
        /// <param name="printerState">The state of the printer.</param>
        public PrinterStatesMessage(IDictionary<string, PrinterStateWithTemperatures> printerState)
            : base("printer_states")
        {
            this.Printers = printerState;
            this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Gets the states of the printers connected to the client.
        /// </summary>
        public IDictionary<string, PrinterStateWithTemperatures> Printers { get; }

        /// <summary>
        /// Gets the time at which the printer states were sampled.
        /// </summary>
        public long Timestamp { get; }
    }
}
