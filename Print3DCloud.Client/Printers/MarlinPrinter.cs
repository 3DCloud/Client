﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.Utilities;
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
        private const string CommandAcknowledgedMessage = "ok";
        private const string ReportTemperaturesCommand = "M105";
        private const string SetLineNumberCommandFormat = "M110 N{0}";
        private const string AutomaticTemperatureReportingCommand = "M155 S{0}";
        private const char LineCommentCharacter = ';';
        private const int TemperatureReportingIntervalSeconds = 1;

        private static readonly Regex CommentRegex = new(@"\(.*?\)|;.*$");
        private static readonly Regex IsTemperatureLineRegex = new(@"T:[\d\.]+ \/[\d\.]+ (?:(?:B|T\d|@\d):[\d\.]+ \/[\d\.]+ ?)+");
        private static readonly Regex TemperaturesRegex = new(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly AutoResetEvent sendCommandResetEvent = new(true);
        private readonly AutoResetEvent commandAcknowledgedResetEvent = new(true);

        private CancellationTokenSource globalCancellationTokenSource;
        private SerialPortStream? serialPort;

        private Task? temperaturePollingTask;
        private Task? receiveLoopTask;
        private Task? printTask;

        private long currentLineNumber;
        private long resendLine;

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
            this.globalCancellationTokenSource = new CancellationTokenSource();
        }

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
        public PrinterState State { get; private set; } = PrinterState.Disconnected;

        /// <inheritdoc/>
        public PrinterTemperatures? Temperatures { get; private set; }

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Disconnected)
            {
                throw new InvalidOperationException("Printer is already connected");
            }

            this.State = PrinterState.Connecting;

            this.logger.LogInformation($"Connecting to Marlin printer at port '{this.PortName}'...");

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

            while (line != CommandAcknowledgedMessage)
            {
                line = await this.ReadLineAsync().ConfigureAwait(false);
            }

            this.currentLineNumber = 1;

            this.logger.LogInformation($"Connected");

            this.globalCancellationTokenSource = new CancellationTokenSource();

            if (!await this.GetPrinterSupportsAutomaticTemperatureReportingAsync(cancellationToken))
            {
                this.temperaturePollingTask = Task.Run(this.TemperaturePolling, cancellationToken).ContinueWith(this.HandleTemperaturePollingTaskCompleted, CancellationToken.None);
            }

            this.receiveLoopTask = Task.Run(this.ReceiveLoop, cancellationToken).ContinueWith(this.HandleReceiveLoopTaskCompleted, CancellationToken.None);

            this.State = PrinterState.Ready;
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
            this.logger.LogDebug("Waiting for all tasks to complete...");

            this.globalCancellationTokenSource.Cancel();

            var tasks = new List<Task>(3);

            if (this.printTask != null) tasks.Add(this.printTask);
            if (this.temperaturePollingTask != null) tasks.Add(this.temperaturePollingTask);
            if (this.receiveLoopTask != null) tasks.Add(this.receiveLoopTask);

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }

            this.Dispose();

            this.logger.LogDebug("Disconnected successfully");
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Ready)
            {
                throw new InvalidOperationException("Printer isn't ready");
            }

            this.State = PrinterState.Printing;

            CancellationTokenSource printCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.globalCancellationTokenSource.Token);

            this.printTask = this.PrintFileAsync(fileStream, printCancellationTokenSource.Token);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task PausePrintAsync(CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Printing)
            {
                throw new InvalidOperationException("Not printing");
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public Task ResumePrintAsync(CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Paused)
            {
                throw new InvalidOperationException("Print isn't paused");
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Printing && this.State != PrinterState.Pausing && this.State != PrinterState.Paused)
            {
                throw new InvalidOperationException("Not printing");
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.serialPort?.Dispose();
            this.serialPort = null;
            this.State = PrinterState.Disconnected;
            this.Temperatures = null;
        }

        private async Task<bool> GetPrinterSupportsAutomaticTemperatureReportingAsync(CancellationToken cancellationToken)
        {
            this.logger.LogTrace("Checking if printer supports automatic temperature reporting");

            await this.WriteLineAsync(string.Format(AutomaticTemperatureReportingCommand, TemperatureReportingIntervalSeconds)).ConfigureAwait(false);
            string? line = null;
            bool result = true;

            while (line != CommandAcknowledgedMessage)
            {
                cancellationToken.ThrowIfCancellationRequested();

                line = await this.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                line = line.Trim();

                if (line.StartsWith("echo:Unknown Command:"))
                {
                    result = false;
                }
            }

            return result;
        }

        private async Task SendCommandInternalAsync(string command, CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Ready)
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
            line += "*" + MarlinUtilities.GetCommandChecksum(line, this.serialPort!.Encoding);

            this.resendLine = this.currentLineNumber;

            while (this.resendLine > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (this.resendLine != this.currentLineNumber)
                {
                    throw new InvalidOperationException($"Printer requested resend of line {this.resendLine} but we last sent {this.currentLineNumber}");
                }

                this.resendLine = 0;

                // same as above
                await this.commandAcknowledgedResetEvent.WaitOneAsync(CancellationToken.None).ConfigureAwait(false);
                await this.WriteLineAsync(line).ConfigureAwait(false);
            }

            this.currentLineNumber++;
        }

        private async Task PrintFileAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            using var fileReader = new StreamReader(fileStream);

            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected)
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

        private async Task<string?> ReadLineAsync()
        {
            if (this.serialPort == null || !this.serialPort.IsOpen)
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
            if (this.serialPort == null || !this.serialPort.IsOpen)
            {
                throw new ObjectDisposedException("Serial port is closed");
            }

            this.logger.LogTrace("SEND: " + line);
            this.LogMessage?.Invoke("SEND: " + line);

            return Task.Run(() => this.serialPort.WriteLine(line));
        }

        private async Task TemperaturePolling()
        {
            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected)
            {
                await this.SendCommandAsync(ReportTemperaturesCommand, this.globalCancellationTokenSource.Token).ConfigureAwait(false);
                await Task.Delay(TemperatureReportingIntervalSeconds * 1000, this.globalCancellationTokenSource.Token).ConfigureAwait(false);
            }
        }

        private async Task HandleTemperaturePollingTaskCompleted(Task task)
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

                await this.DisconnectAsync();
            }
        }

        private async Task ReceiveLoop()
        {
            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected)
            {
                string? line = await this.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                this.HandleLine(line.Trim());
            }
        }

        private async Task HandleReceiveLoopTaskCompleted(Task task)
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

                await this.DisconnectAsync();
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
            else if (line.StartsWith(PrinterAliveLine))
            {
                throw new Exception("Printer restarted unexpectedly");
            }
            else if (line.StartsWith(CommandAcknowledgedMessage))
            {
                this.commandAcknowledgedResetEvent.Set();
            }

            if (IsTemperatureLineRegex.IsMatch(line))
            {
                MatchCollection matches = TemperaturesRegex.Matches(line);

                TemperatureSensor activeHotendTemperature = null!;
                TemperatureSensor? bedTemperature = null;
                var hotendTemperatures = new List<TemperatureSensor>();

                foreach (Match match in matches)
                {
                    string sensor = match.Groups["sensor"].Value;
                    double currentTemperature = double.Parse(match.Groups["current"].Value, CultureInfo.InvariantCulture);
                    double targetTemperature = double.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                    var temperature = new TemperatureSensor(sensor, currentTemperature, targetTemperature);

                    if (sensor == "B")
                    {
                        bedTemperature = temperature;
                    }
                    else if (sensor == "T")
                    {
                        activeHotendTemperature = temperature;
                    }
                    else if (sensor[0] == 'T')
                    {
                        hotendTemperatures.Add(temperature);
                    }
                    else
                    {
                        this.logger.LogWarning($"Unexpected sensor name '{sensor}'");
                    }
                }

                this.Temperatures = new PrinterTemperatures(activeHotendTemperature, hotendTemperatures, bedTemperature);
            }
        }
    }
}
