namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Indicates the type of message received from a Marlin printer.
    /// </summary>
    internal enum MarlinMessageType
    {
        /// <summary>
        /// Standard message.
        /// </summary>
        Message,

        /// <summary>
        /// Printer startup message.
        /// </summary>
        Startup,

        /// <summary>
        /// The last sent command was successfully processed.
        /// </summary>
        CommandAcknowledgement,

        /// <summary>
        /// The last sent command was not recognized.
        /// </summary>
        UnknownCommand,

        /// <summary>
        /// The printer has requested a line to be resent.
        /// </summary>
        ResendLine,

        /// <summary>
        /// A fatal error has occurred and the printer has shut down.
        /// </summary>
        FatalError,
    }
}
