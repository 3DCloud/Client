namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Factory that creates instances of <see cref="SerialPortWrapper"/>.
    /// </summary>
    internal class SerialPortStreamFactory : ISerialPortFactory
    {
        /// <inheritdoc/>
        public ISerialPort CreatePrinterStream(string portName, int baudRate)
        {
            return new SerialPortWrapper(portName, baudRate);
        }
    }
}
