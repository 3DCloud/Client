using System.Threading.Tasks;
using RJCP.IO.Ports;

namespace Print3DCloud.Client
{
    /// <summary>
    /// An extension to the <see cref="SerialPortStream"/> class that adds asynchronous line read/write operations.
    /// </summary>
    internal class AsyncSerialPort : SerialPortStream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncSerialPort"/> class.
        /// </summary>
        /// <param name="portName">The port to use (for example, COM1).</param>
        /// <param name="baudRate">The baud rate.</param>
        public AsyncSerialPort(string portName, int baudRate)
            : base(portName, baudRate)
        {
        }

        /// <summary>
        /// Reads a line from the serial port as an asynchronous operation.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once a line has been read.</returns>
        public Task<string> ReadLineAsync()
        {
            return Task.Run(() =>
            {
                return this.ReadLine();
            });
        }

        /// <summary>
        /// Writes a line to the serial port as an asynchronous operation.
        /// </summary>
        /// <param name="line">The line to write.</param>
        /// <returns>A <see cref="Task"/> that completes once the line has been sent.</returns>
        public Task WriteLineAsync(string line)
        {
            return Task.Run(() =>
            {
                this.WriteLine(line);
            });
        }
    }
}
