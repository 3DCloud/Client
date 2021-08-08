using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RJCP.IO.Ports;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Printer driver for printers using Marlin (or derived) firmware that send G-code via serial.
    /// </summary>
    internal class MarlinPrinter : IPrinter
    {
        /// <summary>
        /// Serial printer driver ID as defined by the back-end.
        /// </summary>
        public const string DriverId = "marlin";

        private const string PrinterAliveLine = "echo:start";
        private const string CommandExpectedResponse = "ok";
        private const string ReportTemperaturesCommand = "M105";
        private const string SetLineNumberCommandFormat = "M110 N{0}";
        private const char LineCommentCharacter = ';';

        private static readonly Regex CommentRegex = new Regex(@"\(.*?\)|;.*$");
        private static readonly Regex IsTemperatureLineRegex = new Regex(@"T:[\d\.]+ \/[\d\.]+ (?:(?:B|T\d|@\d):[\d\.]+ \/[\d\.]+ ?)+");
        private static readonly Regex TemperaturesRegex = new Regex(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly AutoResetEvent sendCommandResetEvent;
        private readonly AutoResetEvent commandAcknowledgedResetEvent;

        private CancellationTokenSource globalCancellationTokenSource;
        private SerialPortStream? serialPort;

        private Task? temperaturePollingTask;
        private Task? receiveLoopTask;
        private Task? printTask;

        private long currentLineNumber;
        private long resendLine;

        private TemperatureSensor activeHotendTemperature;
        private List<TemperatureSensor> hotendTemperatures;
        private TemperatureSensor? bedTemperature;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarlinPrinter"/> class.
        /// </summary>
        /// <param name="logger">The logger that should be used for this printer.</param>
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        public MarlinPrinter(ILogger<MarlinPrinter> logger, string portName, int baudRate = 250_000)
        {
            this.PortName = portName;
            this.BaudRate = baudRate;

            this.logger = logger;
            this.sendCommandResetEvent = new AutoResetEvent(true);
            this.commandAcknowledgedResetEvent = new AutoResetEvent(true);
            this.activeHotendTemperature = new TemperatureSensor(string.Empty, 0, 0);
            this.hotendTemperatures = new List<TemperatureSensor>();
            this.globalCancellationTokenSource = new CancellationTokenSource();
        }

        /// <inheritdoc/>
        public event Action<PrinterState>? StateChanged;

        /// <inheritdoc/>
        public event Action<string>? LogMessage;

        /// <summary>
        /// Gets the name of the serial port.
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// Gets the baud rate used when communicating via serial.
        /// </summary>
        public int BaudRate { get; }

        /// <inheritdoc/>
        public PrinterState State => new PrinterState(
            this.IsConnected,
            this.IsPrinting,
            this.activeHotendTemperature,
            this.hotendTemperatures,
            this.bedTemperature);

        /// <summary>
        /// Gets a value indicating whether or not this printer is currently connected.
        /// </summary>
        [MemberNotNullWhen(true, nameof(serialPort))]
        public bool IsConnected => this.serialPort != null && this.serialPort.IsOpen;

        /// <summary>
        /// Gets a value indicating whether or not this printer is currently printing.
        /// </summary>
        [MemberNotNullWhen(true, nameof(printTask))]
        public bool IsPrinting => this.printTask != null && !this.printTask.IsCompleted;

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"Connecting to Marlin printer at port '{this.PortName}'...");

            this.globalCancellationTokenSource = new CancellationTokenSource();

            this.serialPort = new SerialPortStream(this.PortName, this.BaudRate)
            {
                RtsEnable = true,
                DtrEnable = true,
                NewLine = "\n",
            };

            // clean up anything that's currently there
            this.serialPort.DiscardInBuffer();
            this.serialPort.DiscardOutBuffer();

            this.serialPort.Open();

            cancellationToken.Register(this.Dispose);

            string? line = null;

            while (line != PrinterAliveLine)
            {
                line = await this.ReadLineAsync().ConfigureAwait(false);
            }

            await this.WriteLineAsync(string.Format(SetLineNumberCommandFormat, 0)).ConfigureAwait(false);

            while (line != CommandExpectedResponse)
            {
                line = await this.ReadLineAsync().ConfigureAwait(false);
            }

            this.currentLineNumber = 1;

            this.logger.LogInformation($"Connected");

            this.temperaturePollingTask = Task.Run(this.TemperaturePolling, cancellationToken).ContinueWith(this.HandleTemperaturePollingTaskCompleted, CancellationToken.None);
            this.receiveLoopTask = Task.Run(this.ReceiveLoop, cancellationToken).ContinueWith(this.HandleReceiveLoopTaskCompleted, CancellationToken.None);
        }

        /// <inheritdoc/>
        public async Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            CancellationTokenSource commandTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.globalCancellationTokenSource.Token);

            await this.sendCommandResetEvent.WaitOneAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await this.SendCommandInternalAsync(command, commandTokenSource.Token).ConfigureAwait(false);
            }
            finally
            {
                this.sendCommandResetEvent.Set();
            }
        }

        /// <inheritdoc/>
        public async Task SendCommandBlockAsync(string commands, CancellationToken cancellationToken)
        {
            foreach (string line in commands.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await this.SendCommandAsync(line, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            this.Dispose();

            this.logger.LogDebug("Waiting for all tasks to complete...");

            this.globalCancellationTokenSource.Cancel();

            var tasks = new List<Task>(3);

            if (this.printTask != null) tasks.Add(this.printTask.WaitForCompletionAsync());
            if (this.temperaturePollingTask != null) tasks.Add(this.temperaturePollingTask.WaitForCompletionAsync());
            if (this.receiveLoopTask != null) tasks.Add(this.receiveLoopTask.WaitForCompletionAsync());

            await Task.WhenAll(tasks).ConfigureAwait(false);

            this.logger.LogDebug("Disconnected successfully");
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            if (this.IsPrinting)
            {
                throw new InvalidOperationException("Already printing something");
            }

            CancellationTokenSource printCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.globalCancellationTokenSource.Token);

            return this.printTask = this.StartPrintInternalAsync(fileStream, printCancellationTokenSource.Token);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.serialPort?.Dispose();
            this.serialPort = null;
        }

        private async Task SendCommandInternalAsync(string command, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
            {
                throw new InvalidOperationException("Connection with printer lost");
            }

            // reset current line number if necessary
            if (this.currentLineNumber <= 0 || this.currentLineNumber == long.MaxValue)
            {
                // we don't allow cancelling since not waiting for acknowledgement can make us enter a broken state
                await this.commandAcknowledgedResetEvent.WaitOneAsync(CancellationToken.None).ConfigureAwait(false);
                await this.WriteLineAsync(string.Format(SetLineNumberCommandFormat, 0)).ConfigureAwait(false);

                this.currentLineNumber = 1;
            }

            cancellationToken.ThrowIfCancellationRequested();

            int newLineIndex = command.IndexOf('\n');

            if (newLineIndex >= 0)
            {
                command = command.Substring(0, newLineIndex);
                this.logger.LogInformation($"Command has multiple lines; only the first one ('{command}') will be sent");
            }

            command = CommentRegex.Replace(command, string.Empty).Trim();
            string line = $"N{this.currentLineNumber} {command} N{this.currentLineNumber}";
            line += "*" + this.GetCommandChecksum(line, this.serialPort.Encoding);

            this.resendLine = this.currentLineNumber;

            while (this.resendLine > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (this.resendLine != this.currentLineNumber)
                {
                    throw new InvalidOperationException($"Printer requested line {this.resendLine} but we last sent {this.currentLineNumber}");
                }

                this.resendLine = 0;

                // same as above
                await this.commandAcknowledgedResetEvent.WaitOneAsync(CancellationToken.None).ConfigureAwait(false);
                await this.WriteLineAsync(line).ConfigureAwait(false);
            }

            this.currentLineNumber++;
        }

        private async Task StartPrintInternalAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            using var fileReader = new StreamReader(fileStream);

            while (this.IsConnected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await fileReader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line)) break;

                // ignore full-line comments
                if (line.TrimStart().StartsWith(LineCommentCharacter))
                {
                    continue;
                }

                await this.SendCommandAsync(line, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Calculates a simple checksum for the given command.
        /// Based on Marlin's source code: https://github.com/MarlinFirmware/Marlin/blob/8e1ea6a2fa1b90a58b4257eec9fbc2923adda680/Marlin/src/gcode/queue.cpp#L485.
        /// </summary>
        /// <param name="command">The command for which to generate a checksum.</param>
        /// <param name="encoding">The encoding to use when converting the command to a byte array.</param>
        /// <returns>The command's checksum.</returns>
        private byte GetCommandChecksum(string command, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(command);
            byte checksum = 0;

            foreach (byte b in bytes)
            {
                checksum ^= b;
            }

            return checksum;
        }

        private async Task<string?> ReadLineAsync()
        {
            if (!this.IsConnected)
            {
                throw new ObjectDisposedException("Serial port is closed");
            }

            string? line = await Task.Run(this.serialPort.ReadLine).ConfigureAwait(false);

            this.logger.LogTrace("RECV: " + line);
            this.LogMessage?.Invoke("RECV: " + line);

            return line;
        }

        private Task WriteLineAsync(string line)
        {
            if (!this.IsConnected)
            {
                throw new ObjectDisposedException("Serial port is closed");
            }

            this.logger.LogTrace("SEND: " + line);
            this.LogMessage?.Invoke("SEND: " + line);

            return Task.Run(() => this.serialPort.WriteLine(line));
        }

        // TODO figure out a way to check if printers support automatic temperature reporting https://marlinfw.org/docs/gcode/M155.html
        private async Task TemperaturePolling()
        {
            while (this.IsConnected)
            {
                await this.SendCommandAsync(ReportTemperaturesCommand, CancellationToken.None).ConfigureAwait(false);

                this.StateChanged?.Invoke(this.State);

                await Task.Delay(1000, this.globalCancellationTokenSource.Token).ConfigureAwait(false);
            }
        }

        private void HandleTemperaturePollingTaskCompleted(Task task)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogDebug("Temperature Polling task completed");
            }
            else if (task.IsCanceled)
            {
                this.logger.LogWarning("Temperature Polling task canceled");
            }
            else if (task.IsFaulted)
            {
                this.logger.LogError("Temperature Polling task errored");
                this.logger.LogError(task.Exception!.ToString());
            }
        }

        private async Task ReceiveLoop()
        {
            while (this.IsConnected)
            {
                string? line = await this.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                this.HandleLine(line.Trim());
            }
        }

        private void HandleReceiveLoopTaskCompleted(Task task)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogDebug("Receive Loop task completed");
            }
            else if (task.IsCanceled)
            {
                this.logger.LogWarning("Receive Loop task canceled");
            }
            else if (task.IsFaulted)
            {
                this.logger.LogError("Receive Loop task errored");
                this.logger.LogError(task.Exception!.ToString());
            }
        }

        private void HandleLine(string line)
        {
            if (line.StartsWith("Error:"))
            {
                string errorMessage = line[6..];

                if (errorMessage == "Printer halted. kill() called!")
                {
                    throw new PrinterHaltedException("Printer halted");
                }

                this.logger.LogError(errorMessage);

                return;
            }
            else if (line.StartsWith("Resend:"))
            {
                this.resendLine = int.Parse(line[7..].Trim());
                this.logger.LogWarning("Printer requested resend for line number " + this.resendLine);
            }
            else if (line.StartsWith(CommandExpectedResponse))
            {
                this.commandAcknowledgedResetEvent.Set();
            }

            if (IsTemperatureLineRegex.IsMatch(line))
            {
                MatchCollection matches = TemperaturesRegex.Matches(line);

                this.bedTemperature = null;
                this.hotendTemperatures.Clear();

                foreach (Match match in matches)
                {
                    string sensor = match.Groups["sensor"].Value;
                    double currentTemperature = double.Parse(match.Groups["current"].Value, CultureInfo.InvariantCulture);
                    double targetTemperature = double.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                    var temperature = new TemperatureSensor(sensor, currentTemperature, targetTemperature);

                    if (sensor == "B")
                    {
                        this.bedTemperature = temperature;
                    }
                    else if (sensor == "T")
                    {
                        this.activeHotendTemperature = temperature;
                    }
                    else if (sensor[0] == 'T')
                    {
                        this.hotendTemperatures.Add(temperature);
                    }
                    else
                    {
                        this.logger.LogWarning($"Unexpected sensor name '{sensor}'");
                    }
                }
            }
        }
    }
}
