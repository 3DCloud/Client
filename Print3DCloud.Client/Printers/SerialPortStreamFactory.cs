using RJCP.IO.Ports;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Factory that creates instances of <see cref="SerialPortStream"/>.
    /// </summary>
    internal class SerialPortStreamFactory : ISerialPortStreamFactory
    {
        /// <inheritdoc/>
        public ISerialPortStream CreatePrinterStream(string portName, int baudRate)
        {
            return new SerialPortStream(portName, baudRate)
            {
                RtsEnable = true,
                DtrEnable = true,
                NewLine = "\n",
            };
        }
    }
}
