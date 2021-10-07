using System.Text;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Factory that creates instances of <see cref="SerialPortWrapper"/>.
    /// </summary>
    internal class SerialPortFactory : ISerialPortFactory
    {
        /// <inheritdoc/>
        public ISerialPort CreateSerialPort(string portName, int baudRate)
        {
            return new SerialPortWrapper(portName, baudRate)
            {
                DtrEnable = true,
                RtsEnable = false,
            };
        }
    }
}
