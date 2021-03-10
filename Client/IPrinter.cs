using System;
using System.IO;

namespace Client
{
    /// <summary>
    /// Represents a connected 3D printer.
    /// </summary>
    internal interface IPrinter : IDisposable
    {
        /// <summary>
        /// Gets the printer's identifier.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// Connects to the printer.
        /// </summary>
        void Connect();

        /// <summary>
        /// Disconnects the printer.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Starts a print on this printer.
        /// </summary>
        /// <param name="fileStream">The <see cref="Stream"/> containing the file.</param>
        void StartPrint(Stream fileStream);

        /// <summary>
        /// Aborts the currently running print.
        /// </summary>
        void AbortPrint();

        /// <summary>
        /// Gets the <see cref="PrinterState"/> that represents the state of this printer at the moment the method is called.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        PrinterState GetState();
    }
}
