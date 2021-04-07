using System;
using ActionCableSharp;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// A message containing the state of a specific printer.
    /// </summary>
    internal class PrinterStateMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterStateMessage"/> class.
        /// </summary>
        /// <param name="printerState">The state of the printer.</param>
        public PrinterStateMessage(PrinterState printerState)
            : base("state")
        {
            this.PrinterState = printerState;
            this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Gets the states of the printers connected to the client.
        /// </summary>
        public PrinterState PrinterState { get; }

        /// <summary>
        /// Gets the time at which the printer states were sampled.
        /// </summary>
        public long Timestamp { get; }
    }
}
