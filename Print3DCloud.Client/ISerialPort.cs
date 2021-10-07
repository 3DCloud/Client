using System.IO;
using System.Text;

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
        /// Gets the serial baud rate.
        /// </summary>
        int BaudRate { get; }

        /// <summary>
        /// Gets the byte encoding for pre- and post-transmission conversion of text.
        /// </summary>
        Encoding Encoding { get; }

        /// <summary>
        /// Gets a value indicating whether the serial port is open or not.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Gets a value indicating whether the Data Terminal Ready (DTR) signal is enabled during serial communication.
        /// </summary>
        bool DtrEnable { get; }

        /// <summary>
        /// Gets the value used to interpret the end of a call to the <see cref="ReadLine"/> and <see cref="WriteLine(string)"/> methods.
        /// </summary>
        string NewLine { get; }

        /// <summary>
        /// Gets the port for communications, including but not limited to all available COM ports.
        /// </summary>
        string PortName { get; }

        /// <summary>
        /// Gets a value indicating whether the Request to Send (RTS) signal is enabled during serial communication.
        /// </summary>
        bool RtsEnable { get; }

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
