using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Print3DCloud.Client
{
    /// <summary>
    /// An extension to the <see cref="SerialPort"/> class that adds asynchronous read/write operations.
    /// </summary>
    internal class AsyncSerialPort : SerialPort
    {
        private const int BufferSize = 1024;

        private Decoder decoder;
        private byte[] buffer;
        private char[] charBuffer;

        private int charPos;
        private int charLen;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncSerialPort"/> class.
        /// </summary>
        /// <param name="portName">The port to use (for example, COM1).</param>
        /// <param name="baudRate">The baud rate.</param>
        /// <param name="parity">One of the <see cref="Parity"/> values.</param>
        public AsyncSerialPort(string portName, int baudRate, Parity parity)
            : base(portName, baudRate, parity)
        {
            this.decoder = this.Encoding.GetDecoder();
            this.buffer = new byte[BufferSize];
            this.charBuffer = new char[this.Encoding.GetMaxCharCount(this.buffer.Length)];
        }

        /// <summary>
        /// Gets or sets the byte encoding for pre- and post-transmission conversion of text.
        /// </summary>
        public new Encoding Encoding
        {
            get => base.Encoding;
            set
            {
                base.Encoding = value;

                this.decoder = value.GetDecoder();
                this.buffer = new byte[BufferSize];
                this.charBuffer = new char[value.GetMaxCharCount(this.buffer.Length)];
            }
        }

        /// <summary>
        /// Reads a line from the serial port as an asynchronous operation.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes once a line has been read.</returns>
        public async Task<string> ReadLineAsync()
        {
            var builder = new StringBuilder();

            while (this.IsOpen)
            {
                while (this.charPos < this.charLen)
                {
                    char c = this.charBuffer[this.charPos++];

                    if (c == '\n')
                    {
                        return builder.ToString();
                    }

                    builder.Append(c);
                }

                await this.ReadBuffer();
            }

            throw new ObjectDisposedException("Stream closed");
        }

        /// <summary>
        /// Writes a line to the serial port as an asynchronous operation.
        /// </summary>
        /// <param name="line">The line to write.</param>
        /// <returns>A <see cref="Task"/> that completes once the line has been sent.</returns>
        public async Task WriteLineAsync(string line)
        {
            await this.BaseStream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"));
            await this.BaseStream.FlushAsync();
        }

        private async Task ReadBuffer()
        {
            this.charPos = 0;
            this.charLen = 0;

            while (this.IsOpen && this.charLen == 0)
            {
                // ReadAsync on a SerialStream (what BaseStream is internally) ignores the CancellationToken passed to it, so the only way to stop
                // it once it started is to close BaseStream via this.Close() or this.Dispose()
                // see https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Ports/src/System/IO/Ports/SerialPort.cs
                // and https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Ports/src/System/IO/Ports/SerialStream.cs
                int readCount = await this.BaseStream.ReadAsync(new Memory<byte>(this.buffer, 0, this.buffer.Length));
                this.charLen = this.decoder.GetChars(this.buffer, 0, readCount, this.charBuffer, 0, false);
            }
        }
    }
}
