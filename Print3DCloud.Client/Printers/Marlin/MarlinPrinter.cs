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

        private static readonly Regex TemperaturesRegex = new(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ISerialPortStreamFactory printerStreamFactory;
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
        /// <param name="printerStreamFactory">The factory to use when creating a stream for communicating with the printer.</param>
        /// <param name="logger">The logger that should be used for this printer.</param>
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        public MarlinPrinter(ISerialPortStreamFactory printerStreamFactory, ILogger<MarlinPrinter> logger, string portName, int baudRate = 250_000)
        {
            this.printerStreamFactory = printerStreamFactory;
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

            ISerialPortStream serialPort = this.printerStreamFactory.CreatePrinterStream(this.portName, this.baudRate);

            // clean up anything that's currently there
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            serialPort.Open();

            this.serialCommandProcessor = new SerialCommandManager(this.logger, (Stream)serialPort, serialPort.Encoding, serialPort.NewLine);
            this.globalCancellationTokenSource = new CancellationTokenSource();

            await this.serialCommandProcessor.WaitForStartupAsync(cancellationToken);

            this.logger.LogInformation("Connected");

            this.globalCancellationTokenSource = new CancellationTokenSource();

            if (!await this.GetPrinterSupportsAutomaticTemperatureReportingAsync(this.serialCommandProcessor, cancellationToken))
            {
                this.temperaturePollingTask = Task.Run(() => this.TemperaturePolling(this.globalCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Temperature polling"), CancellationToken.None);
            }

            this.receiveLoopTask = Task.Run(() => this.ReceiveLoop(this.globalCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Receive loop"), CancellationToken.None);

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.serialCommandProcessor == null)
            {
                throw new InvalidOperationException($"Printer isn't connected");
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
            this.State = PrinterState.Disconnecting;

            this.logger.LogDebug("Waiting for all tasks to complete...");

            this.globalCancellationTokenSource?.Cancel();
            this.globalCancellationTokenSource = null;

            this.serialCommandProcessor?.Dispose();
            this.serialCommandProcessor = null;

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

            this.State = PrinterState.Disconnected;
            this.Temperatures = null;

            this.logger.LogDebug("Disconnected successfully");
        }

        /// <inheritdoc/>
        public Task StartPrintAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Ready)
            {
                throw new InvalidOperationException("Printer isn't ready");
            }

            this.State = PrinterState.Printing;
            this.printCancellationTokenSource = new CancellationTokenSource();

            this.printTask = Task.Run(() => this.RunPrintAsync(stream, this.printCancellationTokenSource.Token).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Print")), cancellationToken);

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
            this.Dispose(true);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the System.IO.TextReader and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.State = PrinterState.Disconnected;
                this.Temperatures = null;

                this.serialCommandProcessor?.Dispose();
                this.serialCommandProcessor = null;

                if (this.globalCancellationTokenSource != null)
                {
                    this.globalCancellationTokenSource.Cancel();
                    this.globalCancellationTokenSource.Dispose();
                    this.globalCancellationTokenSource = null;
                }

                if (this.printCancellationTokenSource != null)
                {
                    this.printCancellationTokenSource.Cancel();
                    this.printCancellationTokenSource.Dispose();
                    this.printCancellationTokenSource = null;
                }
            }
        }

        private static TemperatureSensor GetSensorFromMatch(Match match)
        {
            string sensor = match.Groups["sensor"].Value;
            double currentTemperature = double.Parse(match.Groups["current"].Value, CultureInfo.InvariantCulture);
            double targetTemperature = double.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
            return new(sensor, currentTemperature, targetTemperature);
        }

        private async Task<bool> GetPrinterSupportsAutomaticTemperatureReportingAsync(SerialCommandManager serialCommandProcessor, CancellationToken cancellationToken)
        {
            this.logger.LogTrace("Checking if printer supports automatic temperature reporting");

            Task sendCommandTask = serialCommandProcessor.SendCommandAsync(string.Format(AutomaticTemperatureReportingCommand, TemperatureReportingIntervalSeconds), cancellationToken);
            MarlinMessage line;
            bool result = true;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                line = await serialCommandProcessor.ReceiveLineAsync(cancellationToken);

                if (line.Type == MarlinMessageType.UnknownCommand)
                {
                    result = false;
                }
            }
            while (line.Type != MarlinMessageType.CommandAcknowledgement && !sendCommandTask.IsCompleted);

            await sendCommandTask;

            return result;
        }

        private async Task RunPrintAsync(Stream stream, CancellationToken cancellationToken)
        {
            // StreamReader takes care of closing the stream properly
            using var streamReader = new StreamReader(stream);

            while (streamReader.Peek() != -1)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await streamReader.ReadLineAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line)) continue;

                await this.SendCommandAsync(line, cancellationToken).ConfigureAwait(false);
            }

            this.State = PrinterState.Ready;
        }

        private async Task HandleTaskCompletedAsync(Task task, string name)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogDebug($"{name} task completed");
            }
            else if (task.IsCanceled)
            {
                this.logger.LogDebug($"{name} task canceled");
            }
            else if (task.IsFaulted)
            {
                this.logger.LogError($"{name} task errored");
                this.logger.LogError(task.Exception!.ToString());

                await this.DisconnectAsync();
            }
        }

        private async Task TemperaturePolling(CancellationToken cancellationToken)
        {
            while (this.State != PrinterState.Disconnecting && this.State != PrinterState.Disconnected)
            {
                await this.SendCommandAsync(ReportTemperaturesCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TemperatureReportingIntervalSeconds * 1000, cancellationToken).ConfigureAwait(false);
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

                this.HandleLine(line.Content);
            }
        }

        private void HandleLine(string line)
        {
            Match match = TemperaturesRegex.Match(line);

            if (!match.Success)
            {
                return;
            }

            // first match is always active hotend
            TemperatureSensor activeHotendTemperature = GetSensorFromMatch(match);

            TemperatureSensor? bedTemperature = null;
            List<TemperatureSensor> hotendTemperatures = new();

            match = match.NextMatch();

            while (match.Success)
            {
                TemperatureSensor temperature = GetSensorFromMatch(match);

                if (temperature.Name == "B")
                {
                    bedTemperature = temperature;
                }
                else
                {
                    hotendTemperatures.Add(temperature);
                }

                match = match.NextMatch();
            }

            this.Temperatures = new PrinterTemperatures(activeHotendTemperature, hotendTemperatures, bedTemperature);
        }
    }
}
