using System.IO;
using System.IO.Ports;

namespace Print3DCloud.Client
{
    /// <summary>
    /// Representation of a basic serial port.
    /// </summary>
    internal interface ISerialPort
    {
        /// <summary>
        /// Gets the underlying <see cref="Stream"/> object.
        /// </summary>
        Stream BaseStream { get; }

        /// <summary>
        /// Gets or sets the serial baud rate.
        /// </summary>
        int BaudRate { get; set; }

        /// <summary>
        /// Gets a value indicating whether the serial port is open or not.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Gets or sets the parity-checking protocol.
        /// </summary>
        Parity Parity { get; set; }

        /// <summary>
        /// Gets or sets the port for communications, including but not limited to all available COM ports.
        /// </summary>
        string PortName { get; set; }

        /// <summary>
        /// Closes the port connection, sets the <see cref="IsOpen"/> property to false, and disposes of the internal <see cref="Stream"/> object.
        /// </summary>
        void Close();

        /// <summary>
        /// Discards data from the serial driver's receive buffer.
        /// </summary>
        void DiscardInBuffer();

        /// <summary>
        /// Discards data from the serial driver's transmit buffer.
        /// </summary>
        void DiscardOutBuffer();

        /// <summary>
        /// Opens a new serial port connection.
        /// </summary>
        void Open();
    }
}
