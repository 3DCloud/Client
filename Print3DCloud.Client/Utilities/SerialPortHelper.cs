using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Print3DCloud.Client.Utilities;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Helper methods related to serial ports.
    /// </summary>
    internal class SerialPortHelper
    {
        private static readonly Regex ComPortRegex = new Regex(@"\((?<com>COM[0-9]+)\)");
        private static readonly Regex DeviceIdRegex = new Regex(@"^USB\\VID_(?<vid>[0-9a-f]{4})(?:&PID_(?<pid>[0-9a-f]{4}))?(?:&MI_\d{2})?(?:\\(?<identifier>.*))?", RegexOptions.IgnoreCase);

        /// <summary>
        /// Gets information related to the serial ports available on this computer.
        /// </summary>
        /// <returns>A list of <see cref="SerialPortInfo"/>.</returns>
        public static List<SerialPortInfo> GetPorts()
        {
            if (OperatingSystem.IsWindows())
            {
                return GetPorts_Windows();
            }
            else if (OperatingSystem.IsLinux())
            {
                return GetPorts_Linux();
            }
            else
            {
                throw new NotSupportedException($"{nameof(GetPorts)} is not supported on {Environment.OSVersion.Platform:G}");
            }
        }

        [SupportedOSPlatform("windows")]
        private static List<SerialPortInfo> GetPorts_Windows()
        {
            // Win32_SerialPort doesn't find USB serial ports so we have to use Win32_PnPEntity with the "Ports (COM & LPT ports)" GUID
            // (see https://docs.microsoft.com/en-us/windows-hardware/drivers/install/system-defined-device-setup-classes-available-to-vendors)
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");
            ManagementObjectCollection serialPorts = searcher.Get();

            var portInfos = new List<SerialPortInfo>(serialPorts.Count);

            foreach (ManagementBaseObject obj in serialPorts)
            {
                string deviceName = (string)obj["Name"];
                string pnpDeviceId = (string)obj["PNPDeviceID"];

                Match portNameMatch = ComPortRegex.Match(deviceName);
                Match deviceIdMatch = DeviceIdRegex.Match(pnpDeviceId);

                if (!portNameMatch.Success || !deviceIdMatch.Success) continue;

                string portName = portNameMatch.Groups["com"].Value;

                // if the identifier part of the ID isn't alphanumeric, it's an identifier
                // created by Windows that will change depending on the physical port
                string identifier = deviceIdMatch.Groups["identifier"].Value;
                bool isSerialNumber = identifier.All(char.IsLetterOrDigit);

                portInfos.Add(new SerialPortInfo
                {
                    PortName = portName,
                    VendorId = deviceIdMatch.Groups["vid"].Value,
                    ProductId = deviceIdMatch.Groups["pid"].Value,
                    UniqueId = identifier,
                    IsPortableUniqueId = isSerialNumber,
                });
            }

            return portInfos;
        }

        [SupportedOSPlatform("linux")]
        private static List<SerialPortInfo> GetPorts_Linux()
        {
            return new List<SerialPortInfo>();
        }
    }
}
