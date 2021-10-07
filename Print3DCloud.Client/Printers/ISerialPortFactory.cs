namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// A factory that creates instances of a class implementing <see cref="ISerialPort"/>.
    /// </summary>
    internal interface ISerialPortFactory
    {
        /// <summary>
        /// Creates a new instance of a class implementing <see cref="ISerialPort"/>.
        /// </summary>
        /// <param name="portName">The name of the serial port.</param>
        /// <param name="baudRate">The baud rate to use during serial communication.</param>
        /// <returns>A new instance of a class implementing <see cref="ISerialPort"/>.</returns>
        ISerialPort CreatePrinterStream(string portName, int baudRate);
    }
}
