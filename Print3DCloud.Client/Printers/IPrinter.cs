﻿using System;
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
        /// Triggered when the printer's state changes.
        /// </summary>
        public event Action<PrinterState>? StateChanged;

        /// <summary>
        /// Triggered when a message produced by the printer should be logged.
        /// </summary>
        public event Action<string>? LogMessage;

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

        /// <summary>
        /// Sends a command to the printer.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        Task SendCommandAsync(string command, CancellationToken cancellationToken);

        /// <summary>
        /// Sends a block of commands to the printer.
        /// </summary>
        /// <param name="commands">The commands to send, separated by a new line.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the commands have been sent.</returns>
        Task SendCommandBlockAsync(string commands, CancellationToken cancellationToken);
    }
}
