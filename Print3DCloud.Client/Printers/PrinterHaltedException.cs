using System;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// The exception that is thrown when the printer unexpectedly halts (e.g. due to an emergency stop).
    /// </summary>
    internal class PrinterHaltedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterHaltedException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public PrinterHaltedException(string? message)
            : base(message)
        {
        }
    }
}
