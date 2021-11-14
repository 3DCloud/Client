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
        /// The printer is connected.
        /// </summary>
        Connected,

        /// <summary>
        /// The printer is connected and ready to accept commands. Sub-state of <see cref="Connected"/>.
        /// </summary>
        Ready,

        /// <summary>
        /// A file to print is being downloaded. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Downloading,

        /// <summary>
        /// The printer is attempting to disconnect gracefully.
        /// </summary>
        Disconnecting,

        /// <summary>
        /// The printer is busy. Sub-state of <see cref="Connected"/>.
        /// </summary>
        Busy,

        /// <summary>
        /// The printer is heating. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Heating,

        /// <summary>
        /// The printer is printing from a file. Sub-state of <see cref="Busy"/>.
        /// </summary>
        Printing,

        /// <summary>
        /// The printer has received an abort request and is attempting to abort the ongoing print. Sub-state of <see cref="Printing"/>.
        /// </summary>
        Canceling,
    }
}
