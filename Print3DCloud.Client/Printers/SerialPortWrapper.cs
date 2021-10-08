using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Ports;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// A thin wrapper around <see cref="SerialPort"/> that implements the <see cref="ISerialPort"/> interface.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal class SerialPortWrapper : ISerialPort
    {
        private readonly SerialPort serialPort;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerialPortWrapper"/> class.
        /// </summary>
        /// <param name="portName">The port to use (for example, COM1).</param>
        /// <param name="baudRate">The baud rate.</param>
        public SerialPortWrapper(string portName, int baudRate)
        {
            this.serialPort = new SerialPort(portName, baudRate);
        }

        /// <inheritdoc/>
        public Stream BaseStream => this.serialPort.BaseStream;

        /// <inheritdoc/>
        public int BaudRate
        {
            get => this.serialPort.BaudRate;
            set => this.serialPort.BaudRate = value;
        }

        /// <inheritdoc/>
        public bool IsOpen => this.serialPort.IsOpen;

        /// <summary>
        /// Gets or sets a value indicating whether the Data Terminal Ready (DTR) signal is enabled during serial communication.
        /// </summary>
        public bool DtrEnable
        {
            get => this.serialPort.DtrEnable;
            set => this.serialPort.DtrEnable = value;
        }

        /// <inheritdoc/>
        public string PortName
        {
            get => this.serialPort.PortName;
            set => this.serialPort.PortName = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the Request to Send (RTS) signal is enabled during serial communication.
        /// </summary>
        public bool RtsEnable
        {
            get => this.serialPort.RtsEnable;
            set => this.serialPort.RtsEnable = value;
        }

        /// <summary>
        /// Gets or sets the number of milliseconds before a time-out occurs when a write operation does not finish.
        /// </summary>
        public int WriteTimeout
        {
            get => this.serialPort.WriteTimeout;
            set => this.serialPort.WriteTimeout = value;
        }

        /// <inheritdoc/>
        public void Close() => this.serialPort.Close();

        /// <inheritdoc/>
        public void DiscardInBuffer() => this.serialPort.DiscardInBuffer();

        /// <inheritdoc/>
        public void DiscardOutBuffer() => this.serialPort.DiscardOutBuffer();

        /// <inheritdoc/>
        public void Open() => this.serialPort.Open();
    }
}
