using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            using ManagementObjectSearcher searcher = new("SELECT * FROM Win32_PnPEntity WHERE ClassGuid='{4d36e978-e325-11ce-bfc1-08002be10318}'");
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

                portInfos.Add(new SerialPortInfo(
                    portName,
                    deviceIdMatch.Groups["vid"].Value,
                    deviceIdMatch.Groups["pid"].Value,
                    identifier,
                    null));
            }

            return portInfos;
        }

        /// <summary>
        /// Gets serial ports on Linux-based systems.
        /// Based on https://github.com/pyserial/pyserial/blob/master/serial/tools/list_ports_linux.py.
        /// </summary>
        /// <returns>A list of <see cref="SerialPortInfo"/>.</returns>
        [SupportedOSPlatform("linux")]
        private static List<SerialPortInfo> GetPorts_Linux()
        {
            List<SerialPortInfo> portInfos = new();

            foreach (string path in Enumerable.Concat(
                Directory.EnumerateFiles("/dev", "ttyUSB*"),
                Directory.EnumerateFiles("/dev", "ttyACM*")))
            {
                string deviceName = Path.GetFileName(path);
                string ttyPath = Path.Join("/sys/class/tty", deviceName);

                if (!Directory.Exists(ttyPath))
                {
                    continue;
                }

                string realTtyPath = Directory.ResolveLinkTarget(ttyPath, true)!.FullName;
                string realDevicePath = Directory.ResolveLinkTarget(Path.Join(realTtyPath, "device"), true)!.FullName;
                string subsystem = Directory.ResolveLinkTarget(Path.Join(realDevicePath, "subsystem"), true)!.Name;
                string usbInterfacePath;

                switch (subsystem)
                {
                    case "usb":
                        usbInterfacePath = realDevicePath;
                        break;

                    case "usb-serial":
                        usbInterfacePath = Path.GetDirectoryName(realDevicePath)!;
                        break;

                    default:
                        continue;
                }

                usbInterfacePath = Path.GetDirectoryName(usbInterfacePath)!;
                string serialPath = Path.Join(usbInterfacePath, "serial");
                string? serialNumber = null;

                if (File.Exists(serialPath))
                {
                    serialNumber = File.ReadAllText(serialPath).Trim();
                }

                portInfos.Add(new SerialPortInfo(
                    path,
                    File.ReadAllText(Path.Join(usbInterfacePath, "idVendor")),
                    File.ReadAllText(Path.Join(usbInterfacePath, "idProduct")),
                    usbInterfacePath[13..],
                    serialNumber));
            }

            return portInfos;
        }
    }
}
