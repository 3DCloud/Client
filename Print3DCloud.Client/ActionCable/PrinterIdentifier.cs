using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Identifies a printer when connecting to the Printer channel.
    /// </summary>
    internal record PrinterIdentifier : Identifier
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrinterIdentifier"/> class.
        /// </summary>
        /// <param name="hardwareIdentifier">The hardware identifier for the printer to be represented by this identifier.</param>
        public PrinterIdentifier(string hardwareIdentifier)
            : base("PrinterChannel")
        {
            this.HardwareIdentifier = hardwareIdentifier;
        }

        /// <summary>
        /// Gets the hardware identifier for the printer represented by this identifier.
        /// </summary>
        public string HardwareIdentifier { get; }
    }
}
