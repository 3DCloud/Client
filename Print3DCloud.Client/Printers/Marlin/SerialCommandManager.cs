using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Manages sending commands and waiting for acknowledgement.
    /// </summary>
    internal class SerialCommandManager : IDisposable
    {
        private const string CommandAcknowledgedMessage = "ok";
        private const string UnknownCommandMessage = "echo:Unknown command:";
        private const string PrinterAliveMessage = "start";
        private const char LineCommentCharacter = ';';
        private const int SendCommandMaxRetries = 5;

        private static readonly byte[] SetLineNumberCommand = Encoding.ASCII.GetBytes("N0 M110 N0*125\n");

        private readonly ILogger logger;
        private readonly Stream stream;
        private readonly Encoding encoding;
        private readonly string newLine;

        private readonly StreamReader streamReader;

        private readonly SemaphoreSlim sendCommandSemaphore = new(1, 1);
        private readonly SemaphoreSlim commandAcknowledgedSemaphore = new(0, 1);

        private int currentLineNumber;
        private int resendLine;

        private bool connected;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerialCommandManager"/> class.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger{TCategoryName}"/> to use.</param>
        /// <param name="stream">The <see cref="Stream"/> to use to communicate with the printer.</param>
        /// <param name="encoding">The <see cref="Encoding"/> to use when communicating with the printer.</param>
        /// <param name="newLine">The new line character(s) to use when communicating with the printer.</param>
        public SerialCommandManager(ILogger logger, Stream stream, Encoding encoding, string newLine)
        {
            if (string.IsNullOrEmpty(newLine)) throw new ArgumentNullException(nameof(newLine));

            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            this.newLine = newLine;

            this.streamReader = new StreamReader(stream, encoding);
        }

        /// <summary>
        /// Waits for the machine to produce a startup message.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the printer has successfully started up.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
        public async Task WaitForStartupAsync(CancellationToken cancellationToken)
        {
            string? line = null;

            while (line == null || !line.EndsWith(PrinterAliveMessage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                line = await this.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            await this.WriteLineAsync(SetLineNumberCommand, cancellationToken).ConfigureAwait(false);

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

            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            command = command.Trim();

            int index = command.IndexOf(LineCommentCharacter);

            if (index == 0)
            {
                return;
            }
            else if (index != -1)
            {
                command = command[..index].TrimEnd();
            }

            await this.sendCommandSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // reset current line number if necessary
                if (this.currentLineNumber == int.MaxValue)
                {
                    await this.SendAndWaitForAcknowledgementAsync(SetLineNumberCommand, cancellationToken).ConfigureAwait(false);

                    this.currentLineNumber = 1;
                }

                cancellationToken.ThrowIfCancellationRequested();

                int newLineIndex = command.IndexOf('\n');

                if (newLineIndex >= 0)
                {
                    command = command[..newLineIndex];
                    this.logger.LogInformation($"Command has multiple lines; only the first one ('{command}') will be sent");
                }

                byte[] line = this.BuildCommand(command);

                this.resendLine = this.currentLineNumber;
                int tries = 0;

                while (this.resendLine > 0)
                {
                    if (++tries > SendCommandMaxRetries)
                    {
                        throw new IOException("Printer requested resend too many times");
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (this.resendLine != this.currentLineNumber)
                    {
                        throw new InvalidOperationException($"Printer requested resend of line {this.resendLine} but we last sent {this.currentLineNumber}");
                    }

                    this.resendLine = 0;

                    await this.SendAndWaitForAcknowledgementAsync(line, cancellationToken).ConfigureAwait(false);
                }

                this.currentLineNumber++;
            }
            finally
            {
                this.sendCommandSemaphore.Release();
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

            string line = await this.ReadLineAsync(cancellationToken);

            if (line.StartsWith(CommandAcknowledgedMessage))
            {
                this.commandAcknowledgedSemaphore.Release();
                return new MarlinMessage(line, MarlinMessageType.CommandAcknowledgement);
            }
            else if (line == PrinterAliveMessage)
            {
                throw new PrinterHaltedException("Printer restarted unexpectedly");
            }
            else if (line.StartsWith("Error:"))
            {
                string errorMessage = line[6..];

                if (errorMessage == "Printer halted. kill() called!")
                {
                    throw new PrinterHaltedException(errorMessage);
                }

                return new MarlinMessage(line, MarlinMessageType.Error);
            }
            else if (line.StartsWith("Resend:"))
            {
                this.resendLine = int.Parse(line[7..].Trim());
                this.logger.LogWarning("Printer requested resend for line number " + this.resendLine);
                return new MarlinMessage(line, MarlinMessageType.ResendLine);
            }
            else if (line.StartsWith(UnknownCommandMessage))
            {
                this.logger.LogWarning(line);
                return new MarlinMessage(line, MarlinMessageType.UnknownCommand);
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
        /// Cleans up managed objects that implement IDisposable (if <paramref name="disposing"/> is true) and unmanaged resources/objects.
        /// </summary>
        /// <param name="disposing">Whether this is being called from a Dispose method or not.</param>
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.streamReader.Dispose();
                this.stream.Dispose();
                this.sendCommandSemaphore.Dispose();
                this.commandAcknowledgedSemaphore.Dispose();
            }
        }

        /// <summary>
        /// Calculates a simple checksum for the given command.
        /// Based on Marlin's source code: https://github.com/MarlinFirmware/Marlin/blob/8e1ea6a2fa1b90a58b4257eec9fbc2923adda680/Marlin/src/gcode/queue.cpp#L485.
        /// </summary>
        /// <param name="data">The command for which to generate a checksum.</param>
        /// <returns>The command's checksum.</returns>
        private static byte GetCommandChecksum(IEnumerable<byte> data)
        {
            byte checksum = 0;

            foreach (byte b in data)
            {
                checksum ^= b;
            }

            return checksum;
        }

        private byte[] BuildCommand(string command)
        {
            byte[] commandBytes = this.encoding.GetBytes("N" + this.currentLineNumber + " " + command);

            byte checksum = GetCommandChecksum(commandBytes);
            byte[] checksumBytes = this.encoding.GetBytes("*" + checksum + this.newLine);

            byte[] fullCommandBytes = new byte[commandBytes.Length + checksumBytes.Length];
            commandBytes.CopyTo(fullCommandBytes, 0);
            checksumBytes.CopyTo(fullCommandBytes, commandBytes.Length);

            return fullCommandBytes;
        }

        private async Task SendAndWaitForAcknowledgementAsync(byte[] command, CancellationToken cancellationToken)
        {
            await this.WriteLineAsync(command, cancellationToken);
            await this.commandAcknowledgedSemaphore.WaitAsync(CancellationToken.None);
        }

        private async Task WriteLineAsync(byte[] line, CancellationToken cancellationToken)
        {
            await this.stream.WriteAsync(line, cancellationToken);
            await this.stream.FlushAsync(cancellationToken);
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            string? line;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                line = await this.streamReader.ReadLineAsync();
            }
            while (string.IsNullOrWhiteSpace(line));

            return line.Trim();
        }
    }
}