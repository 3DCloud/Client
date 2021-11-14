using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Interface between a physical machine and the server.
    /// </summary>
    internal abstract class Printer : IDisposable
    {
        /// <summary>
        /// Gets or sets the <see cref="PrinterState"/> that represents the current state of this printer.
        /// </summary>
        /// <returns>The state of the printer.</returns>
        public virtual PrinterState State { get; protected set; }

        /// <summary>
        /// Gets or sets the <see cref="PrinterTemperatures"/> containing the latest temperatures reported by the printer.
        /// </summary>
        public virtual PrinterTemperatures? Temperatures { get; protected set; }

        /// <summary>
        /// Gets or sets the estimated amount of time remaining for the ongoing print.
        /// </summary>
        public virtual int? TimeRemaining { get; protected set; }

        /// <summary>
        /// Gets or sets the estimated percentage completion of the ongoing print.
        /// </summary>
        public virtual double? Progress { get; protected set; }

        /// <summary>
        /// Checks whether the printer is in the specified state. Includes sub-states.
        /// </summary>
        /// <param name="printerState">The printer state to compare against.</param>
        /// <returns>Whether the printer is in the specified state or not.</returns>
        public bool IsInState(PrinterState printerState)
        {
            switch (printerState)
            {
                // states with no sub-states
                case PrinterState.Disconnected:
                case PrinterState.Connecting:
                case PrinterState.Disconnecting:
                case PrinterState.Downloading:
                case PrinterState.Heating:
                case PrinterState.Canceling:
                case PrinterState.Ready:
                    return this.State == printerState;

                case PrinterState.Connected:
                    return this.State is
                        PrinterState.Ready or
                        PrinterState.Busy or
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating or
                        PrinterState.Canceling;

                case PrinterState.Busy:
                    return this.State is
                        PrinterState.Busy or
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating or
                        PrinterState.Canceling;

                case PrinterState.Printing:
                    return this.State is
                        PrinterState.Downloading or
                        PrinterState.Printing or
                        PrinterState.Heating;
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Connects to the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public abstract Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Disconnects the printer.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public abstract Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Runs a print on this printer.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing the file to print.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been started.</returns>
        public abstract Task ExecutePrintAsync(Stream stream, CancellationToken cancellationToken);

        /// <summary>
        /// Aborts the print that is currently running.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the print has been aborted.</returns>
        public abstract Task AbortPrintAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Sends a command to the printer.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        public abstract Task SendCommandAsync(string command, CancellationToken cancellationToken);

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Printer"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);
    }
}
