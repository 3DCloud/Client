using System;
using System.IO;
using System.IO.Ports;
using System.Text;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// A thin wrapper around <see cref="SerialPort"/> that implements the <see cref="ISerialPort"/> interface.
    /// </summary>
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
            this.serialPort = new SerialPort(portName, baudRate)
            {
                DtrEnable = true,
                NewLine = "\n",
                RtsEnable = false,
            };
        }

        /// <inheritdoc/>
        public Stream BaseStream => this.serialPort.BaseStream;

        /// <inheritdoc/>
        public int BaudRate => this.serialPort.BaudRate;

        /// <inheritdoc/>
        public Encoding Encoding => this.serialPort.Encoding;

        /// <inheritdoc/>
        public bool IsOpen => this.serialPort.IsOpen;

        /// <inheritdoc/>
        public bool DtrEnable => this.serialPort.DtrEnable;

        /// <inheritdoc/>
        public string NewLine => this.serialPort.NewLine;

        /// <inheritdoc/>
        public string PortName => this.serialPort.PortName;

        /// <inheritdoc/>
        public bool RtsEnable => this.serialPort.RtsEnable;

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
