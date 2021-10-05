using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Print3DCloud.Client.Tests
{
    /// <summary>
    /// A memory stream that contains separate input and output data.
    /// </summary>
    internal class SerialPrinterStreamSimulator : Stream
    {
        private readonly MemoryStream inputStream = new();
        private readonly MemoryStream outputStream = new();

        private readonly List<ResponseMatch> responses = new();
        private readonly Decoder decoder = Encoding.ASCII.GetDecoder();
        private readonly char[] chars = new char[1024];

        private StringBuilder stringBuilder = new();

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
        public override long Length => this.inputStream.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => this.inputStream.Position;
            set => this.inputStream.Position = value;
        }

        /// <summary>
        /// Make the printer send a message.
        /// </summary>
        /// <param name="message">Message the printer sends.</param>
        public void SendMessage(string message)
        {
            lock (this.inputStream)
            {
                byte[] data = this.Encoding.GetBytes(message + '\n');
                this.inputStream.Write(data);
                this.inputStream.Seek(-data.Length, SeekOrigin.Current);
            }
        }

        /// <summary>
        /// Registers a response to be sent when <paramref name="receivedMessage"/> is received.
        /// </summary>
        /// <param name="receivedMessage">Message that triggers the response.</param>
        /// <param name="responseMessage">Message to send when <paramref name="receivedMessage"/> is received.</param>
        /// <param name="times">The number of messages to which the <paramref name="responseMessage"/> will be sent.</param>
        public void RegisterResponse(string receivedMessage, string responseMessage, int times = -1)
        {
            // this doesn't need exceptional performance since it's only used for tests
            this.responses.Add(new ResponseMatch(new Regex(Regex.Escape(receivedMessage)), responseMessage, times));
        }

        /// <summary>
        /// Registers a response to be sent when a message matching <paramref name="regex"/> is received.
        /// </summary>
        /// <param name="regex">Message that triggers the response.</param>
        /// <param name="responseMessage">Message to send when a message matching <paramref name="regex"/> is received.</param>
        /// <param name="times">The number of messages to which the <paramref name="responseMessage"/> will be sent.</param>
        public void RegisterResponse(Regex regex, string responseMessage, int times = -1)
        {
            this.responses.Add(new ResponseMatch(regex, responseMessage, times));
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (this.inputStream)
            {
                return this.inputStream.Read(buffer, offset, count);
            }
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
            lock (this.outputStream)
            {
                this.outputStream.Write(buffer, offset, count);

                int charCount = this.decoder.GetChars(buffer, offset, count, this.chars, 0, false);

                for (int i = 0; i < charCount; i++)
                {
                    char c = this.chars[i];

                    if (c == '\n')
                    {
                        string str = this.stringBuilder.ToString();
                        this.stringBuilder = new StringBuilder();
                        ResponseMatch? responseMatch = this.responses.FirstOrDefault(t => t.Times != 0 && t.Regex.IsMatch(str));

                        if (responseMatch != null)
                        {
                            lock (this.inputStream)
                            {
                                long prevPosition = this.inputStream.Position;
                                this.inputStream.Position = this.inputStream.Length;

                                string line = responseMatch.Regex.Replace(str, responseMatch.Response) + '\n';
                                this.inputStream.Write(this.Encoding.GetBytes(line));

                                this.inputStream.Position = prevPosition;

                                --responseMatch.Times;
                            }
                        }
                    }
                    else
                    {
                        this.stringBuilder.Append(c);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the lines written to the printer.
        /// </summary>
        /// <returns>The lines written to the printer.</returns>
        public string[] GetWrittenLines()
        {
            byte[] data = this.outputStream.ToArray();

            // Encoding.GetString returns an empty string
            // for an empty array but we don't want that
            if (data.Length == 0)
            {
                return Array.Empty<string>();
            }

            string str = this.Encoding.GetString(data);

            // trim last newline if necessary; we don't want to trim all newlines
            // since that would indicate something is sending empty messages
            if (str.EndsWith('\n'))
            {
                str = str[..^1];
            }

            return str.Split('\n');
        }

        /// <inheritdoc/>
        public override void Close()
        {
            lock (this.inputStream)
            {
                this.inputStream.Close();
            }

            lock (this.outputStream)
            {
                this.outputStream.Close();
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (this.inputStream)
                {
                    this.inputStream.Dispose();
                }

                lock (this.outputStream)
                {
                    this.outputStream.Dispose();
                }
            }
        }

        private record ResponseMatch
        {
            public ResponseMatch(Regex regex, string response, int times)
            {
                this.Regex = regex;
                this.Response = response;
                this.Times = times;
            }

            public Regex Regex { get; }

            public string Response { get; }

            public int Times { get; set; }
        }
    }
}
