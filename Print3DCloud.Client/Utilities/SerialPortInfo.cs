using System;

namespace Print3DCloud.Client.Utilities
{
    /// <summary>
    /// Contains information for identifying a serial port on this machine.
    /// </summary>
    internal struct SerialPortInfo
    {
        /// <summary>
        /// Gets the name of this serial port. COM[0-9]+ on Windows and /dev/tty* on Linux.
        /// </summary>
        public string PortName { get; init; }

        /// <summary>
        /// Gets the Vendor ID of the device connected at this serial port.
        /// </summary>
        public string? VendorId { get; init; }

        /// <summary>
        /// Gets the Product ID of the device connected at this serial port.
        /// </summary>
        public string? ProductId { get; init; }

        /// <summary>
        /// Gets a unique ID representing the device connected at this serial port. Usually either the device's serial number (portable ID) or a system identifier (non-portable ID).
        /// </summary>
        public string UniqueId { get; init; }

        /// <summary>
        /// Gets a value indicating whether or not the <see cref="UniqueId"/> will be the same across multiple ports/clients.
        /// </summary>
        public bool IsPortableUniqueId { get; init; }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(this.PortName, this.VendorId, this.ProductId, this.UniqueId, this.IsPortableUniqueId);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj == null || obj is not SerialPortInfo other) return false;

            return this.PortName == other.PortName && this.VendorId == other.VendorId && this.ProductId == other.ProductId && this.UniqueId == other.UniqueId && this.IsPortableUniqueId == other.IsPortableUniqueId;
        }
    }
}
