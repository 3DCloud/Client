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
        /// <param name="name">The name of the device.</param>
        /// <param name="path">The path of the device in the system device tree.</param>
        /// <param name="serialNumber">The serial number of the device, if any.</param>
        public DeviceMessage(string name, string path, string? serialNumber)
            : base("device")
        {
            this.Name = name;
            this.Path = path;
            this.SerialNumber = serialNumber;
        }

        /// <summary>
        /// Gets the name of the device.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the path of the device in the system device tree.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the serial number of the device, if any.
        /// </summary>
        public string? SerialNumber { get; }
    }
}
