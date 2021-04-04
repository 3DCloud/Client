using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a connected 3D printer.
    /// </summary>
    internal interface IPrinter : IDisposable
    {
        /// <summary>
        /// Gets the <see cref="PrinterState"/> that represents the current state of this printer.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        public PrinterState State { get; }

        /// <summary>
        /// Connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects the printer.
        /// </summary>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Starts a print on this printer.
        /// </summary>
        /// <param name="fileStream">The <see cref="Stream"/> containing the file.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been started.</returns>
        Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken);
    }
}
