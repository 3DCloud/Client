using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Printer driver for printers using Marlin (or derived) firmware that send G-code via serial.
    /// </summary>
    internal class MarlinPrinter : IPrinter
    {
        private const string PrinterAliveLine = "echo:start";
        private const string HelloCommand = "G0";
        private const string CommandExpectedResponse = "ok";
        private const string ReportTemperaturesCommand = "M105";

        private static readonly Regex CommentRegex = new Regex(@"\(.*?\)|;.*$");
        private static readonly Regex IsTemperatureLineRegex = new Regex(@"T:[\d\.]+ \/[\d\.]+ (?:(?:B|T\d|@\d):[\d\.]+ \/[\d\.]+ ?)+");
        private static readonly Regex TemperaturesRegex = new Regex(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly SemaphoreSlim sendCommandSemaphore;
        private readonly SemaphoreSlim commandAcknowledgedSemaphore;

        private CancellationTokenSource globalCancellationTokenSource;
        private CancellationTokenSource printCancellationTokenSource;
        private AsyncSerialPort? serialPort;

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
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        /// <param name="parity">The type of parity checking to use when communicating.</param>
        public MarlinPrinter(string portName, int baudRate = 250000, Parity parity = Parity.None)
        {
            this.PortName = portName;
            this.BaudRate = baudRate;
            this.Parity = parity;

            this.logger = Logging.LoggerFactory.CreateLogger<MarlinPrinter>();
            this.sendCommandSemaphore = new SemaphoreSlim(1);
            this.commandAcknowledgedSemaphore = new SemaphoreSlim(1);
            this.hotendTemperatures = new List<TemperatureSensor>();
            this.globalCancellationTokenSource = new CancellationTokenSource();
            this.printCancellationTokenSource = new CancellationTokenSource();
        }

        /// <inheritdoc/>
        public string Identifier => this.PortName;

        /// <summary>
        /// Gets the name of the serial port.
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// Gets the baud rate used when communicating via serial.
        /// </summary>
        public int BaudRate { get; }

        /// <summary>
        /// Gets the <see cref="Parity"/> used when communicating via serial.
        /// </summary>
        public Parity Parity { get; }

        /// <inheritdoc/>
        public PrinterState State => new PrinterState
        {
            IsConnected = this.IsConnected,
            IsPrinting = this.IsPrinting,
            ActiveHotendTemperature = this.activeHotendTemperature,
            HotendTemperatures = this.hotendTemperatures.ToArray(),
            BuildPlateTemperature = this.bedTemperature,
        };

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

            this.serialPort = new AsyncSerialPort(this.PortName, this.BaudRate, this.Parity)
            {
                RtsEnable = true,
                DtrEnable = true,
                NewLine = "\n",
            };

            this.serialPort.Open();

            cancellationToken.Register(() => this.Dispose());

            string? line = null;

            while (line != PrinterAliveLine)
            {
                line = await this.ReadLineAsync();
            }

            await this.WriteLineAsync(HelloCommand, cancellationToken);

            while (line != CommandExpectedResponse)
            {
                line = await this.ReadLineAsync();
            }

            this.logger.LogInformation($"Connected");

            this.temperaturePollingTask = Task.Run(this.TemperaturePolling, cancellationToken).ContinueWith(this.HandleTemperaturePollingCompleted);
            this.receiveLoopTask = Task.Run(this.ReceiveLoop, cancellationToken).ContinueWith(this.HandleReceiveLoopCompleted);
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            CancellationTokenSource commandTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.globalCancellationTokenSource.Token);

            return this.SendCommandInternalAsync(command, commandTokenSource.Token);
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

            await Task.WhenAll(tasks);
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            if (this.IsPrinting)
            {
                throw new InvalidOperationException("Already printing something");
            }

            this.printCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.globalCancellationTokenSource.Token);

            return this.printTask = this.StartPrintInternalAsync(fileStream, this.printCancellationTokenSource.Token);
        }

        /// <inheritdoc/>
        public async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            if (!this.IsPrinting) throw new InvalidOperationException("No print is currently running.");

            this.printCancellationTokenSource?.Cancel();
            await this.printTask.WaitForCompletionAsync();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.serialPort?.Dispose();
        }

        private async Task SendCommandInternalAsync(string command, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
            {
                throw new InvalidOperationException("Connection with printer lost");
            }

            await this.sendCommandSemaphore.WaitAsync(cancellationToken);

            // reset current line number if necessary
            if (this.currentLineNumber <= 0 || this.currentLineNumber == long.MaxValue)
            {
                await this.WriteLineAsync("M110 N0", cancellationToken);
                await this.commandAcknowledgedSemaphore.WaitAsync(cancellationToken);

                this.currentLineNumber = 1;
            }

            string line = $"N{this.currentLineNumber} {CommentRegex.Replace(command, string.Empty).Trim()} N{this.currentLineNumber}";
            line += "*" + this.GetCommandChecksum(line, this.serialPort.Encoding);

            this.resendLine = this.currentLineNumber;

            while (this.resendLine > 0)
            {
                if (this.resendLine != this.currentLineNumber)
                {
                    throw new InvalidOperationException($"Printer requested line {this.resendLine} but we last sent {this.currentLineNumber}");
                }

                this.resendLine = 0;

                await this.WriteLineAsync(line, cancellationToken);
                await this.commandAcknowledgedSemaphore.WaitAsync(cancellationToken);
            }

            this.currentLineNumber++;
            this.sendCommandSemaphore.Release();
        }

        private async Task StartPrintInternalAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            using var fileReader = new StreamReader(fileStream);

            while (this.IsConnected)
            {
                string? line = await fileReader.ReadLineAsync();

                if (line == null) break;

                // ignore empty lines and full-line comments
                if (line.Trim().StartsWith(';'))
                {
                    continue;
                }

                await this.SendCommandInternalAsync(line, cancellationToken);
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

        private async Task<string> ReadLineAsync()
        {
            if (!this.IsConnected)
            {
                throw new ObjectDisposedException("Serial port is closed");
            }

            string line = await this.serialPort.ReadLineAsync();

            this.logger.LogTrace("RECV: " + line);

            return line;
        }

        private Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
            {
                throw new ObjectDisposedException("Serial port is closed");
            }

            this.logger.LogTrace("SEND: " + line);

            return this.serialPort.WriteLineAsync(line, cancellationToken);
        }

        private async Task TemperaturePolling()
        {
            while (this.IsConnected)
            {
                await this.SendCommandInternalAsync(ReportTemperaturesCommand, this.globalCancellationTokenSource.Token);

                await Task.Delay(1000, this.globalCancellationTokenSource.Token);
            }
        }

        private void HandleTemperaturePollingCompleted(Task task)
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
                string? line = await this.ReadLineAsync();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                this.HandleLine(line);
            }
        }

        private void HandleReceiveLoopCompleted(Task task)
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
                    throw new Exception("Printer halted");
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
                this.commandAcknowledgedSemaphore.Release();
            }
            else if (line.StartsWith("echo:"))
            {
                this.logger.LogDebug(line[5..]);
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
                    var temperature = new TemperatureSensor { Name = sensor, Current = currentTemperature, Target = targetTemperature };

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
