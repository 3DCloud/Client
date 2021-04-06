using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message sent when a new device is connected.
    /// </summary>
    internal class DeviceMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceMessage"/> class.
        /// </summary>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="hardwareIdentifier">The unique identifier for this device.</param>
        /// <param name="isPortableHardwareIdentifier">Whether or not the <paramref name="hardwareIdentifier"/> will be the same across multiple clients.</param>
        public DeviceMessage(string deviceName, string hardwareIdentifier, bool isPortableHardwareIdentifier)
            : base("device")
        {
            this.DeviceName = deviceName;
            this.HardwareIdentifier = hardwareIdentifier;
            this.IsPortableHardwareIdentifier = isPortableHardwareIdentifier;
        }

        /// <summary>
        /// Gets the name of the device.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Gets the unique identifier for this device.
        /// </summary>
        public string HardwareIdentifier { get; }

        /// <summary>
        /// Gets a value indicating whether or not the <see cref="HardwareIdentifier"/> will be the same across multiple clients.
        /// </summary>
        public bool IsPortableHardwareIdentifier { get; }
    }
}
