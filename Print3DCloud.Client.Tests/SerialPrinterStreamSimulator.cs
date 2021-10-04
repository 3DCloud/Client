using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Print3DCloud.Client.Tests
{
    /// <summary>
    /// A memory stream that contains separate input and output data.
    /// </summary>
    internal class SerialPrinterStreamSimulator : Stream
    {
        private readonly Dictionary<string, string> responses = new();
        private readonly Decoder decoder = Encoding.ASCII.GetDecoder();
        private readonly char[] chars = new char[1024];

        private StringBuilder stringBuilder = new();

        /// <summary>
        /// Gets the stream from which data is read.
        /// </summary>
        public MemoryStream InputStream { get; } = new MemoryStream();

        /// <summary>
        /// Gets the stream to which data is written.
        /// </summary>
        public MemoryStream OutputStream { get; } = new MemoryStream();

        /// <summary>
        /// Gets the encoding to use while communicating.
        /// </summary>
        public Encoding Encoding { get; init; } = Encoding.ASCII;

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => this.InputStream.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => this.InputStream.Position;
            set => this.InputStream.Position = value;
        }

        /// <summary>
        /// Make the printer send a message.
        /// </summary>
        /// <param name="message">Message the printer sends.</param>
        public void SendMessage(string message)
        {
            byte[] data = this.Encoding.GetBytes(message + '\n');
            this.InputStream.Write(data);
            this.InputStream.Seek(-data.Length, SeekOrigin.Current);
        }

        /// <summary>
        /// Registers a response to be sent when <paramref name="receivedMessage"/> is received.
        /// </summary>
        /// <param name="receivedMessage">Message that triggers the response.</param>
        /// <param name="responseMessage">Message to send when <paramref name="receivedMessage"/> is received.</param>
        public void RespondTo(string receivedMessage, string responseMessage)
        {
            this.responses.Add(receivedMessage, responseMessage);
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            this.InputStream.Flush();
            this.OutputStream.Flush();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.InputStream.Read(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.OutputStream.Write(buffer, offset, count);

            int charCount = this.decoder.GetChars(buffer, offset, count, this.chars, 0, false);

            for (int i = 0; i < charCount; i++)
            {
                char c = this.chars[i];

                if (c == '\n')
                {
                    string str = this.stringBuilder.ToString();
                    this.stringBuilder = new StringBuilder();

                    if (this.responses.TryGetValue(str, out string value))
                    {
                        long prevPosition = this.InputStream.Position;
                        this.InputStream.Position = this.InputStream.Length;

                        this.InputStream.Write(this.Encoding.GetBytes(value + '\n'));
                        this.InputStream.Flush();

                        this.InputStream.Position = prevPosition;
                    }
                }
                else
                {
                    this.stringBuilder.Append(c);
                }
            }
        }

        /// <summary>
        /// Gets the lines written to the printer.
        /// </summary>
        /// <returns>The lines written to the printer.</returns>
        public string[] GetWrittenLines()
        {
            return this.Encoding.GetString(this.OutputStream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <inheritdoc/>
        public override void Close()
        {
            this.InputStream.Close();
            this.OutputStream.Close();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.InputStream.Dispose();
                this.OutputStream.Dispose();
            }
        }
    }
}
