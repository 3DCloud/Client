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
        /// <param name="deviceId">The unique identifier for this device.</param>
        /// <param name="isPortableDeviceId">Whether or not the <paramref name="deviceId"/> will be the same across multiple clients.</param>
        public DeviceMessage(string deviceName, string deviceId, bool isPortableDeviceId)
            : base("device")
        {
            this.DeviceName = deviceName;
            this.DeviceId = deviceId;
            this.IsPortableDeviceId = isPortableDeviceId;
        }

        /// <summary>
        /// Gets the name of the device.
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// Gets the unique identifier for this device.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// Gets a value indicating whether or not the <see cref="DeviceId"/> will be the same across multiple clients.
        /// </summary>
        public bool IsPortableDeviceId { get; }
    }
}
