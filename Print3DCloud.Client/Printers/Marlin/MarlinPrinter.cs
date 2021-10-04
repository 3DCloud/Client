using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RJCP.IO.Ports;

namespace Print3DCloud.Client.Printers.Marlin
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

        private const string ReportTemperaturesCommand = "M105";
        private const string AutomaticTemperatureReportingCommand = "M155 S{0}";
        private const int TemperatureReportingIntervalSeconds = 1;

        private static readonly Regex IsTemperatureLineRegex = new(@"T:[\d\.]+ \/[\d\.]+ (?:(?:B|T\d|@\d):[\d\.]+ \/[\d\.]+ ?)+");
        private static readonly Regex TemperaturesRegex = new(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ILogger<MarlinPrinter> logger;
        private readonly string portName;
        private readonly int baudRate;

        private SerialCommandManager? serialCommandProcessor;
        private CancellationTokenSource? globalCancellationTokenSource;
        private CancellationTokenSource? printCancellationTokenSource;

        private Task? temperaturePollingTask;
        private Task? receiveLoopTask;
        private Task? printTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarlinPrinter"/> class.
        /// </summary>
        /// <param name="logger">The logger that should be used for this printer.</param>
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        public MarlinPrinter(ILogger<MarlinPrinter> logger, string portName, int baudRate = 250_000)
        {
            this.logger = logger;
            this.portName = portName;
            this.baudRate = baudRate;
        }

        /// <inheritdoc/>
        public event Action<string>? LogMessage;

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

            this.logger.LogInformation($"Connecting to Marlin printer at port '{this.portName}'...");

            SerialPortStream serialPort = new(this.portName, this.baudRate)
            {
                RtsEnable = true,
                DtrEnable = true,
                NewLine = "\n",
            };

            // clean up anything that's currently there
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            serialPort.Open();

            this.serialCommandProcessor = new SerialCommandManager(this.logger, serialPort, serialPort.Encoding, serialPort.NewLine);
            this.globalCancellationTokenSource = new CancellationTokenSource();

            await this.serialCommandProcessor.WaitForStartupAsync(cancellationToken);

            this.logger.LogInformation("Connected");

            this.globalCancellationTokenSource = new CancellationTokenSource();

            if (!await this.GetPrinterSupportsAutomaticTemperatureReportingAsync(cancellationToken))
            {
                this.temperaturePollingTask = Task.Run(() => this.TemperaturePolling(this.globalCancellationTokenSource.Token), cancellationToken).ContinueWith(this.HandleTemperaturePollingTaskCompleted, CancellationToken.None);
            }

            this.receiveLoopTask = Task.Run(() => this.ReceiveLoop(this.globalCancellationTokenSource.Token), cancellationToken).ContinueWith(this.HandleReceiveLoopTaskCompleted, CancellationToken.None);

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.serialCommandProcessor == null)
            {
                throw new NullReferenceException($"{nameof(this.serialCommandProcessor)} is not defined");
            }

            return this.serialCommandProcessor.SendCommandAsync(command, cancellationToken);
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

            this.globalCancellationTokenSource?.Cancel();
            this.globalCancellationTokenSource = null;

            var tasks = new List<Task>(3);

            if (this.printTask != null) tasks.Add(this.printTask);
            if (this.temperaturePollingTask != null) tasks.Add(this.temperaturePollingTask);
            if (this.receiveLoopTask != null) tasks.Add(this.receiveLoopTask);

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
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
            this.printCancellationTokenSource = new CancellationTokenSource();

            this.printTask = this.PrintFileAsync(fileStream, this.printCancellationTokenSource.Token);

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
            this.State = PrinterState.Disconnected;
            this.Temperatures = null;
        }

        private async Task<bool> GetPrinterSupportsAutomaticTemperatureReportingAsync(CancellationToken cancellationToken)
        {
            if (this.serialCommandProcessor == null)
            {
                throw new NullReferenceException($"{nameof(this.serialCommandProcessor)} is not defined");
            }

            this.logger.LogTrace("Checking if printer supports automatic temperature reporting");

            await this.serialCommandProcessor.SendCommandAsync(string.Format(AutomaticTemperatureReportingCommand, TemperatureReportingIntervalSeconds), cancellationToken).ConfigureAwait(false);
            MarlinMessage line;
            bool result = true;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                line = await this.serialCommandProcessor.ReceiveLineAsync(cancellationToken).ConfigureAwait(false);

                if (line.Type == MarlinMessageType.UnknownCommand)
                {
                    result = false;
                }
            }
            while (line?.Type != MarlinMessageType.CommandAcknowledgement);

            return result;
        }

        private async Task PrintFileAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            using var streamReader = new StreamReader(fileStream);

            try
            {
                while (streamReader.Peek() != -1)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = await streamReader.ReadLineAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    await this.SendCommandAsync(line, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex?.ToString());
            }

            this.State = PrinterState.Ready;
        }

        private async Task TemperaturePolling(CancellationToken cancellationToken)
        {
            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected)
            {
                await this.SendCommandAsync(ReportTemperaturesCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TemperatureReportingIntervalSeconds * 1000, cancellationToken).ConfigureAwait(false);
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

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected && this.serialCommandProcessor != null)
            {
                MarlinMessage line = await this.serialCommandProcessor.ReceiveLineAsync(cancellationToken).ConfigureAwait(false);

                if (line.Type != MarlinMessageType.Message)
                {
                    continue;
                }

                this.HandleLine(line.Message);
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

                this.State = PrinterState.Errored;
            }
        }

        private void HandleLine(string line)
        {
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
