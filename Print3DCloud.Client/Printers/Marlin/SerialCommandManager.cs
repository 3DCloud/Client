using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RJCP.IO.Ports;

namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Manages sending commands and waiting for acknowledgement.
    /// </summary>
    internal class SerialCommandManager : IDisposable
    {
        private const string CommandAcknowledgedMessage = "ok";
        private const string UnknownCommandMessage = "echo:Unknown Command:";
        private const string PrinterAliveMessage = "start";
        private const string SetLineNumberCommandFormat = "M110 N{0}";
        private const char LineCommentCharacter = ';';

        private static readonly Regex CommentRegex = new(@"\(.*?\)|;.*$");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly StreamReader streamReader;
        private readonly StreamWriter streamWriter;

        private readonly AutoResetEvent sendCommandResetEvent = new(true);
        private readonly AutoResetEvent commandAcknowledgedResetEvent = new(true);

        private long currentLineNumber;
        private long resendLine;

        private bool connected;
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerialCommandManager"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        /// <param name="serialPort">The <see cref="SerialPortStream"/> to use to communicate with the printer.</param>
        public SerialCommandManager(ILogger<MarlinPrinter> logger, SerialPortStream serialPort)
        {
            this.logger = logger;
            this.streamReader = new StreamReader(serialPort, serialPort.Encoding);
            this.streamWriter = new StreamWriter(serialPort, serialPort.Encoding)
            {
                NewLine = serialPort.NewLine,
                AutoFlush = true,
            };
        }

        /// <summary>
        /// Waits for the machine to produce a startup message.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the printer has successfully started up.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
        public async Task WaitForStartupAsync(CancellationToken cancellationToken)
        {
            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(SerialCommandManager));
            }

            string? line = null;

            while (line == null || !line.EndsWith(PrinterAliveMessage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                line = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            await this.WriteLineAsync(string.Format(SetLineNumberCommandFormat, 0)).ConfigureAwait(false);

            while (line != CommandAcknowledgedMessage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                line = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            this.currentLineNumber = 1;

            this.connected = true;
        }

        /// <summary>
        /// Sends a command via the serial port.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command has been sent.</returns>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="WaitForStartupAsync(CancellationToken)"/> hasn't been called or has not completed successfully.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
        public async Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (!this.connected)
            {
                throw new InvalidOperationException("Startup did not complete");
            }

            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(SerialCommandManager));
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            command = command.Trim();

            if (command.StartsWith(LineCommentCharacter))
            {
                return;
            }

            await this.sendCommandResetEvent.WaitOneAsync(cancellationToken);

            try
            {
                // reset current line number if necessary
                if (this.currentLineNumber <= 0 || this.currentLineNumber == long.MaxValue)
                {
                    // we don't allow cancelling since not waiting for acknowledgement can make us enter a broken state
                    await this.WaitAndSendAsync(string.Format(SetLineNumberCommandFormat, 0), CancellationToken.None).ConfigureAwait(false);

                    this.currentLineNumber = 1;
                }

                cancellationToken.ThrowIfCancellationRequested();

                int newLineIndex = command.IndexOf('\n');

                if (newLineIndex >= 0)
                {
                    command = command[..newLineIndex];
                    this.logger.LogInformation($"Command has multiple lines; only the first one ('{command}') will be sent");
                }

                command = CommentRegex.Replace(command, string.Empty).Trim();
                string line = $"N{this.currentLineNumber} {command} N{this.currentLineNumber}";
                line += "*" + GetCommandChecksum(line, this.streamWriter.Encoding);

                this.resendLine = this.currentLineNumber;

                while (this.resendLine > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (this.resendLine != this.currentLineNumber)
                    {
                        throw new InvalidOperationException($"Printer requested resend of line {this.resendLine} but we last sent {this.currentLineNumber}");
                    }

                    this.resendLine = 0;

                    await this.WaitAndSendAsync(line, cancellationToken).ConfigureAwait(false);
                }

                this.currentLineNumber++;
            }
            finally
            {
                this.sendCommandResetEvent.Set();
            }
        }

        /// <summary>
        /// Reads a line from the serial port.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once a valid line has been received.</returns>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="WaitForStartupAsync(CancellationToken)"/> hasn't been called or has not completed successfully.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
        public async Task<MarlinMessage> ReceiveLineAsync(CancellationToken cancellationToken)
        {
            if (!this.connected)
            {
                throw new InvalidOperationException("Startup did not complete");
            }

            if (this.disposed)
            {
                throw new ObjectDisposedException(nameof(SerialCommandManager));
            }

            string? line = await this.ReadLineAsync(cancellationToken);

            if (line.StartsWith("Error:"))
            {
                string errorMessage = line[6..];

                if (errorMessage == "Printer halted. kill() called!")
                {
                    throw new PrinterHaltedException(errorMessage);
                }

                return new MarlinMessage(line, MarlinMessageType.FatalError);
            }
            else if (line.StartsWith("Resend:"))
            {
                this.resendLine = int.Parse(line[7..].Trim());
                this.logger.LogWarning("Printer requested resend for line number " + this.resendLine);
                return new MarlinMessage(line, MarlinMessageType.ResendLine);
            }
            else if (line.StartsWith(UnknownCommandMessage))
            {
                this.logger.LogWarning("Printer restarted unexpectedly");
                return new MarlinMessage(line, MarlinMessageType.UnknownCommand);
            }
            else if (line.StartsWith(CommandAcknowledgedMessage))
            {
                this.commandAcknowledgedResetEvent.Set();
                return new MarlinMessage(line, MarlinMessageType.CommandAcknowledgement);
            }

            return new MarlinMessage(line, MarlinMessageType.Message);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Cleans up managed objects that implement IDiposable (if <paramref name="disposing"/> is true) and unmanaged resources/objects.
        /// </summary>
        /// <param name="disposing">Whether this is being called from a Dispose method or not.</param>
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.streamWriter.Dispose();
                this.streamReader.Dispose();
            }

            this.disposed = true;
        }

        /// <summary>
        /// Calculates a simple checksum for the given command.
        /// Based on Marlin's source code: https://github.com/MarlinFirmware/Marlin/blob/8e1ea6a2fa1b90a58b4257eec9fbc2923adda680/Marlin/src/gcode/queue.cpp#L485.
        /// </summary>
        /// <param name="command">The command for which to generate a checksum.</param>
        /// <param name="encoding">The encoding to use when converting the command to a byte array.</param>
        /// <returns>The command's checksum.</returns>
        private static byte GetCommandChecksum(string command, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(command);
            byte checksum = 0;

            foreach (byte b in bytes)
            {
                checksum ^= b;
            }

            return checksum;
        }

        private async Task WaitAndSendAsync(string command, CancellationToken cancellationToken)
        {
            // since a call to ReceiveLineAsync is necessary for a command acknowledgement
            // to be processed, we don't want to send a command and wait for the acknowledgement
            // to come since that would force the use of two threads at all times
            // instead, we wait for the previous command to be acknowledged before sending the next one
            await this.commandAcknowledgedResetEvent.WaitOneAsync(cancellationToken);
            await this.WriteLineAsync(command);
        }

        private async Task WriteLineAsync(string line)
        {
            this.logger.LogTrace($"Sending: {line}");

            try
            {
                await this.streamWriter.WriteLineAsync(line);
            }
            catch (ObjectDisposedException)
            {
                this.Dispose();
                throw;
            }
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            string? line = null;

            while (string.IsNullOrEmpty(line))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    line = await this.streamReader.ReadLineAsync();
                }
                catch (ObjectDisposedException)
                {
                    this.Dispose();
                    throw;
                }
            }

            line = line.Trim();

            this.logger.LogTrace($"Received: {line}");

            return line;
        }
    }
}