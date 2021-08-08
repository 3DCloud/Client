using System.Text;

namespace Print3DCloud.Client.Utilities
{
    /// <summary>
    /// Various utility methods related to communication with Marlin-based printers.
    /// </summary>
    internal static class MarlinUtilities
    {
        /// <summary>
        /// Calculates a simple checksum for the given command.
        /// Based on Marlin's source code: https://github.com/MarlinFirmware/Marlin/blob/8e1ea6a2fa1b90a58b4257eec9fbc2923adda680/Marlin/src/gcode/queue.cpp#L485.
        /// </summary>
        /// <param name="command">The command for which to generate a checksum.</param>
        /// <param name="encoding">The encoding to use when converting the command to a byte array.</param>
        /// <returns>The command's checksum.</returns>
        public static byte GetCommandChecksum(string command, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(command);
            byte checksum = 0;

            foreach (byte b in bytes)
            {
                checksum ^= b;
            }

            return checksum;
        }
    }
}
