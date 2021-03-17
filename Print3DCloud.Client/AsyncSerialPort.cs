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

        private readonly Decoder decoder;
        private readonly byte[] buffer;
        private readonly char[] charBuffer;

        private int charPos;
        private int charLen;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncSerialPort"/> class.
        /// </summary>
        /// <param name="portName">The port to use (for example, COM1).</param>
        /// <param name="baudRate">The baud rate.</param>
        /// <param name="parity">One of the <see cref="Parity"/> values.</param>
        /// <param name="encoding">An <see cref="Encoding"/>.</param>
        public AsyncSerialPort(string portName, int baudRate, Parity parity, Encoding encoding)
            : base(portName, baudRate, parity)
        {
            this.decoder = encoding.GetDecoder();
            this.buffer = new byte[BufferSize];
            this.charBuffer = new char[encoding.GetMaxCharCount(this.buffer.Length)];
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
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the line has been sent.</returns>
        public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await this.BaseStream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), cancellationToken);
            await this.BaseStream.FlushAsync(cancellationToken);
        }

        private async Task ReadBuffer()
        {
            this.charPos = 0;
            this.charLen = 0;

            while (this.IsOpen && this.charLen == 0)
            {
                int readCount = await this.BaseStream.ReadAsync(new Memory<byte>(this.buffer, 0, this.buffer.Length));
                this.charLen = this.decoder.GetChars(this.buffer, 0, readCount, this.charBuffer, 0, false);
            }
        }
    }
}
