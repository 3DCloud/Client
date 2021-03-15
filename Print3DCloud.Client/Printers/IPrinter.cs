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
        /// Gets the printer's identifier.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// Connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sends a G-code command to the printer.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        Task SendCommandAsync(string command);

        /// <summary>
        /// Starts a print on this printer.
        /// </summary>
        /// <param name="fileStream">The <see cref="Stream"/> containing the file.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been started.</returns>
        Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken);

        /// <summary>
        /// Aborts the currently running print.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once the print has been aborted.</returns>
        Task AbortPrintAsync();

        /// <summary>
        /// Gets the <see cref="PrinterState"/> that represents the state of this printer at the moment the method is called.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        PrinterState GetState();
    }
}
