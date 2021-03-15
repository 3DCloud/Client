using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Print3DCloud.Tasks;

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
        private readonly SerialPort serialPort;
        private readonly SequentialTaskRunner commandQueue;

        private CancellationTokenSource? cancellationTokenSource;
        private StreamReader? reader;
        private StreamWriter? writer;

        private long currentLineNumber;
        private bool resendLastCommand;
        private bool sendNextCommand;

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
            this.logger = Logging.LoggerFactory.CreateLogger<MarlinPrinter>();

            this.serialPort = new SerialPort(portName, baudRate, parity)
            {
                RtsEnable = true,
                DtrEnable = true,
                NewLine = "\n",
            };

            this.commandQueue = new SequentialTaskRunner();
            this.hotendTemperatures = new List<TemperatureSensor>();
        }

        /// <inheritdoc/>
        public string Identifier => this.serialPort.PortName;

        /// <summary>
        /// Gets a value indicating whether or not this printer is currently connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"Connecting to Marlin printer at port '{this.serialPort.PortName}'...");

            this.serialPort.Open();

            this.cancellationTokenSource = new CancellationTokenSource();
            this.reader = new StreamReader(this.serialPort.BaseStream, Encoding.UTF8, false, -1, true);
            this.writer = new StreamWriter(this.serialPort.BaseStream, Encoding.UTF8, -1, true)
            {
                AutoFlush = true, // we want lines to be sent immediately
            };

            string? line = null;

            while (line != PrinterAliveLine)
            {
                line = await this.reader.ReadLineAsync();
            }

            await this.writer.WriteLineAsync(HelloCommand);

            while (line != CommandExpectedResponse)
            {
                line = await this.reader.ReadLineAsync();
            }

            this.logger.LogInformation($"Connected");

            this.sendNextCommand = true;

            _ = Task.Run(this.TemperaturePolling);
            _ = Task.Run(this.ReceiveLoop);
        }

        /// <inheritdoc/>
        public async Task SendCommandAsync(string command)
        {
            while (this.commandQueue.TaskCount > 100)
            {
                await Task.Delay(10);
            }

            await this.commandQueue.Enqueue(() => this.SendCommandInternalAsync(command));
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            this.IsConnected = false;

            this.writer?.Dispose();
            this.reader?.Dispose();

            this.writer = null;
            this.reader = null;

            this.serialPort.Close();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StartPrintAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(fileStream);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await reader.ReadLineAsync();

                // ignore empty lines and full-line comments
                if (string.IsNullOrEmpty(line) || line.Trim().StartsWith(';'))
                {
                    continue;
                }

                await this.SendCommandAsync(line);
            }
        }

        /// <inheritdoc/>
        public Task AbortPrintAsync()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public PrinterState GetState()
        {
            return new PrinterState
            {
                IsConnected = this.IsConnected,
                IsPrinting = false,
                ActiveHotendTemperature = this.activeHotendTemperature,
                HotendTemperatures = this.hotendTemperatures.ToArray(),
                BuildPlateTemperature = this.bedTemperature,
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.IsConnected = false;

            this.reader?.Dispose();
            this.writer?.Dispose();

            this.serialPort.Dispose();
        }

        /// <summary>
        /// Sends a command along with a line number and checksum to ensure the right command is received and run.
        /// Do not call this directly - only through <see cref="SequentialTaskRunner"/>! It guarantees this will be called sequentially.
        /// </summary>
        /// <param name="command">The G-code command to run.</param>
        /// <returns>A task that completes once the command has been sent and acknowledged by the printer.</returns>
        private async Task SendCommandInternalAsync(string command)
        {
            if (!this.serialPort.IsOpen || this.writer == null)
            {
                throw new InvalidOperationException("Connection with printer lost");
            }

            // reset current line number if necessary
            if (this.currentLineNumber <= 0 || this.currentLineNumber == long.MaxValue)
            {
                this.sendNextCommand = false;
                await this.WriteLineAsync("M110 N0");

                while (!this.sendNextCommand)
                {
                    await Task.Delay(10);
                }

                this.currentLineNumber = 1;
            }

            string line = $"N{this.currentLineNumber} {CommentRegex.Replace(command, string.Empty)} N{this.currentLineNumber}";
            line += "*" + this.GetCommandChecksum(line);

            this.resendLastCommand = true;

            while (!this.sendNextCommand || this.resendLastCommand)
            {
                if (this.resendLastCommand)
                {
                    this.sendNextCommand = false;
                    this.resendLastCommand = false;
                    await this.WriteLineAsync(line);
                }

                await Task.Delay(10);
            }

            this.currentLineNumber++;
        }

        private byte GetCommandChecksum(string command)
        {
            byte checksum = 0;

            foreach (char c in command)
            {
                checksum ^= (byte)c;
            }

            return checksum;
        }

        private async Task WriteLineAsync(string line)
        {
            if (this.writer == null)
            {
                throw new NullReferenceException("Writer is null");
            }

            await this.writer.WriteLineAsync(line);

            this.logger.LogTrace("SEND: " + line);
        }

        private async Task TemperaturePolling()
        {
            try
            {
                while (this.serialPort.IsOpen && this.cancellationTokenSource?.IsCancellationRequested == false)
                {
                    await this.SendCommandAsync(ReportTemperaturesCommand);

                    await Task.Delay(1000);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError("Unexpected error occured in printer temperature polling loop");
                this.logger.LogError(ex.ToString());

                await this.DisconnectAsync(CancellationToken.None);
            }
            finally
            {
                await this.DisconnectAsync(CancellationToken.None);
            }
        }

        private async Task ReceiveLoop()
        {
            try
            {
                while (this.serialPort.IsOpen && this.reader != null && this.cancellationTokenSource?.IsCancellationRequested == false)
                {
                    string? line = await this.reader.ReadLineAsync();

                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    await this.HandleLine(line);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.LogError("Unexpected error occured in printer receive loop");
                this.logger.LogError(ex.ToString());
            }
            finally
            {
                await this.DisconnectAsync(CancellationToken.None);
            }
        }

        private async Task HandleLine(string line)
        {
            this.logger.LogTrace("RECV: " + line);

            if (line.StartsWith("Error:"))
            {
                string errorMessage = line[6..];

                if (errorMessage == "Printer halted. kill() called!")
                {
                    await this.DisconnectAsync(CancellationToken.None);
                }

                this.logger.LogError(errorMessage);

                return;
            }
            else if (line.StartsWith("Resend:"))
            {
                int lineNumber = int.Parse(line[7..].Trim());
                this.logger.LogWarning("Printer requested resend for line number " + lineNumber);
                this.resendLastCommand = true;
            }
            else if (line.StartsWith(CommandExpectedResponse))
            {
                this.sendNextCommand = true;
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
