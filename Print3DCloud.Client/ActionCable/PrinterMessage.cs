using System;
using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// A message containing the state of a specific printer.
    /// </summary>
    internal class PrinterMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterMessage"/> class.
        /// </summary>
        /// <param name="message">The message to broadcast.</param>
        public PrinterMessage(string message)
            : base("log_message")
        {
            this.Message = message;
            this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Gets the states of the printers connected to the client.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the time at which the printer states were sampled.
        /// </summary>
        public long Timestamp { get; }
    }
}
