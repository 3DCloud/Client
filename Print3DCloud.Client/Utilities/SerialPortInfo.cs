using System;

namespace Print3DCloud.Client.Utilities
{
    /// <summary>
    /// Contains information for identifying a serial port on this machine.
    /// </summary>
    internal record SerialPortInfo(string PortName, string? VendorId, string? ProductId, string DevicePath, string? SerialNumber);
}
