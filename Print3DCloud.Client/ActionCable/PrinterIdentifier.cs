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
        /// <param name="devicePath">The device path for the printer to be represented by this identifier.</param>
        public PrinterIdentifier(string devicePath)
            : base("PrinterChannel")
        {
            this.DevicePath = devicePath;
        }

        /// <summary>
        /// Gets the device path for the printer represented by this identifier.
        /// </summary>
        public string DevicePath { get; }
    }
}
