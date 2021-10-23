namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// The various possible states of a printer.
    /// </summary>
    internal enum PrinterState
    {
        /// <summary>
        /// The printer is not connected.
        /// </summary>
        Disconnected,

        /// <summary>
        /// A connection to the printer is being attempted.
        /// </summary>
        Connecting,

        /// <summary>
        /// The printer is connected and ready to accept commands.
        /// </summary>
        Ready,

        /// <summary>
        /// A file to print is being downloaded.
        /// </summary>
        Downloading,

        /// <summary>
        /// The printer is attempting to disconnect gracefully.
        /// </summary>
        Disconnecting,

        /// <summary>
        /// The printer is busy. Sub-state of <see cref="Ready"/>.
        /// </summary>
        Busy,

        /// <summary>
        /// The printer is heating. Sub-state of <see cref="Ready"/>.
        /// </summary>
        Heating,

        /// <summary>
        /// The printer is printing from a file.
        /// </summary>
        Printing,

        /// <summary>
        /// The printer has received a pause request and is attempting to pause the ongoing print. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Pausing,

        /// <summary>
        /// The printer has paused the current print. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Paused,

        /// <summary>
        /// The printer has received a resume request and is attempting to resume the ongoing print. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Resuming,

        /// <summary>
        /// The printer has received an abort request and is attempting to abort the ongoing print. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Canceling,

        /// <summary>
        /// An error has occurred and the printer cannot accept commands until it is reconnected.
        /// </summary>
        Errored,
    }
}
