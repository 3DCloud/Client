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

        private static readonly Regex IsTemperatureLineRegex = new Regex(@"T:[\d\.]+ \/[\d\.]+ (?:(?:B|T\d|@\d):[\d\.]+ \/[\d\.]+ ?)+");
        private static readonly Regex TemperaturesRegex = new Regex(@"(?<sensor>B|T\d):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly SerialPort serialPort;
        private readonly SequentialTaskRunner commandQueue;

        private CancellationTokenSource? cancellationTokenSource;
        private StreamReader? reader;
        private StreamWriter? writer;
        private bool sendNextCommand;

        private List<double> hotendTemperatures;
        private double? bedTemperature;

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
            this.hotendTemperatures = new List<double>();
        }

        /// <inheritdoc/>
        public string Identifier => throw new NotImplementedException();

        /// <summary>
        /// Gets a value indicating whether or not this printer is currently connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
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

            this.logger.LogInformation($"Connected to printer at {this.serialPort.PortName}");

            this.sendNextCommand = true;

            _ = Task.Run(this.TemperaturePolling);
            _ = Task.Run(this.ReceiveLoop);
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command)
        {
            return this.commandQueue.Enqueue(() => this.SendCommandInternalAsync(command));
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
        public Task StartPrintAsync(Stream fileStream)
        {
            throw new NotImplementedException();
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
                HotendTemperatures = this.hotendTemperatures.ToArray(),
                BedTemperature = this.bedTemperature,
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

        private async Task SendCommandInternalAsync(string command)
        {
            if (!this.serialPort.IsOpen || this.writer == null)
            {
                throw new InvalidOperationException("Connection with printer lost");
            }

            while (!this.sendNextCommand)
            {
                await Task.Delay(50);
            }

            this.sendNextCommand = false;

            await this.writer.WriteLineAsync(command);

            this.logger.LogTrace("SEND: " + command);
        }

        private async Task TemperaturePolling()
        {
            try
            {
                while (this.serialPort.IsOpen && this.writer != null && this.cancellationTokenSource?.IsCancellationRequested == false)
                {
                    await this.writer.WriteLineAsync(ReportTemperaturesCommand);

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
                        break;
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

            if (line == CommandExpectedResponse)
            {
                this.sendNextCommand = true;
            }
            else if (line.StartsWith("Error:"))
            {
                string errorMessage = line[6..];
                await this.DisconnectAsync(CancellationToken.None);
            }
            else if (IsTemperatureLineRegex.IsMatch(line))
            {
                MatchCollection matches = TemperaturesRegex.Matches(line);

                this.hotendTemperatures.Clear();

                foreach (Match match in matches)
                {
                    string sensor = match.Groups["sensor"].Value;
                    double currentTemperature = double.Parse(match.Groups["current"].Value, CultureInfo.InvariantCulture);
                    double targetTemperature = double.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);

                    if (sensor == "B")
                    {
                        this.bedTemperature = currentTemperature;
                    }
                    else if (sensor[0] == 'T')
                    {
                        this.hotendTemperatures.Add(currentTemperature);
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
