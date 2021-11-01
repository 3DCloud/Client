using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
        private const string FirmwareInfoCommand = "M115";
        private const string ReportSettingsCommand = "M503";
        private const string AutomaticTemperatureReportingCommand = "M155 S{0}";
        private const int TemperatureReportingIntervalSeconds = 1;
        private const int MaxConnectRetries = 5;
        private const int RetryConnectDelayMs = 1000;

        private static readonly Regex PerAxisCommandRegex = new(@"X(\d+(?:\.\d+)?) Y(\d+(?:\.\d+)?) Z(\d+(?:\.\d+)?) E(\d+(?:\.\d+)?)");
        private static readonly Regex AccelerationCommandRegex = new(@"P(\d+(?:\.\d+)?) R(\d+(?:\.\d+)?) T(\d+(?:\.\d+)?)");
        private static readonly Regex FirmwareInfoRegex = new(@"^FIRMWARE_NAME:(?<firmware_name>.*) SOURCE_CODE_URL:.* PROTOCOL_VERSION:.* MACHINE_TYPE:.* EXTRUDER_COUNT:(?<extruder_count>\d+) UUID:(?<uuid>.*)$");
        private static readonly Regex TemperaturesRegex = new(@"(?<sensor>B|T\d?):(?<current>[\d\.]+) \/(?<target>[\d\.]+)");

        private readonly ISerialPortFactory serialPortFactory;
        private readonly ILogger<MarlinPrinter> logger;
        private readonly string portName;
        private readonly int baudRate;

        private ISerialPort? serialPort;
        private SerialCommandManager? serialCommandManager;
        private CancellationTokenSource? backgroundTaskCancellationTokenSource;
        private CancellationTokenSource? printCancellationTokenSource;

        private Task? temperaturePollingTask;
        private Task? receiveLoopTask;
        private Task? printTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarlinPrinter"/> class.
        /// </summary>
        /// <param name="serialPortFactory">The factory to use when creating a stream for communicating with the printer.</param>
        /// <param name="logger">The logger that should be used for this printer.</param>
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        public MarlinPrinter(ISerialPortFactory serialPortFactory, ILogger<MarlinPrinter> logger, string portName, int baudRate = 250_000)
        {
            this.serialPortFactory = serialPortFactory;
            this.logger = logger;
            this.portName = portName;
            this.baudRate = baudRate;
        }

        /// <inheritdoc/>
        public PrinterState State { get; private set; }

        /// <inheritdoc/>
        public PrinterTemperatures? Temperatures { get; private set; }

        /// <summary>
        /// Gets the printer's settings.
        /// </summary>
        public MarlinSettings? Settings { get; private set; }

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (this.State is not PrinterState.Disconnecting and not PrinterState.Disconnected)
            {
                throw new InvalidOperationException("Printer is already connected");
            }

            this.State = PrinterState.Connecting;

            SerialCommandManager serialCommandManager;
            int tries = 0;

            while (true)
            {
                try
                {
                    this.logger.LogInformation("Connecting to Marlin printer at port '{PortName}'...", this.portName);

                    this.serialPort = this.serialPortFactory.CreateSerialPort(this.portName, this.baudRate);

                    this.serialPort.Open();

                    this.serialPort.DiscardInBuffer();
                    this.serialPort.DiscardOutBuffer();

                    serialCommandManager =
                        new SerialCommandManager(this.logger, this.serialPort.BaseStream, Encoding.UTF8, "\n");

                    await serialCommandManager.WaitForStartupAsync(cancellationToken);

                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Failed to connect to printer\n{Exception}", ex);

                    if (++tries >= MaxConnectRetries)
                    {
                        this.State = PrinterState.Disconnected;
                        throw;
                    }

                    await Task.Delay(RetryConnectDelayMs, cancellationToken);
                }
            }

            this.backgroundTaskCancellationTokenSource = new CancellationTokenSource();

            this.Settings = await this.GetSettingsAsync(serialCommandManager, cancellationToken);
            MarlinFirmwareInfo firmwareInfo =
                await this.GetFirmwareInfoAsync(serialCommandManager, cancellationToken);

            this.serialCommandManager = serialCommandManager;

            this.receiveLoopTask = Task.Run(() => this.ReceiveLoop(this.backgroundTaskCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Receive loop"), CancellationToken.None);

            if (firmwareInfo.CanAutoReportTemperatures)
            {
                await this.SendCommandAsync(string.Format(AutomaticTemperatureReportingCommand, TemperatureReportingIntervalSeconds), cancellationToken);
            }
            else
            {
                this.temperaturePollingTask = Task.Run(() => this.TemperaturePolling(this.backgroundTaskCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Temperature polling"), CancellationToken.None);
            }

            this.logger.LogInformation("Connected successfully");

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.serialCommandManager == null)
            {
                throw new InvalidOperationException("Printer isn't connected");
            }

            return this.serialCommandManager.SendCommandAsync(command, cancellationToken);
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
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (this.State is PrinterState.Disconnecting or PrinterState.Disconnected)
            {
                return;
            }

            this.State = PrinterState.Disconnecting;

            this.logger.LogDebug("Waiting for all tasks to complete...");

            this.serialCommandManager?.Dispose();
            this.serialCommandManager = null;

            this.printCancellationTokenSource?.Cancel();
            this.printCancellationTokenSource = null;

            if (this.printTask != null)
            {
                await this.printTask;
            }

            this.backgroundTaskCancellationTokenSource?.Cancel();
            this.backgroundTaskCancellationTokenSource = null;

            if (this.temperaturePollingTask != null)
            {
                await this.temperaturePollingTask;
            }

            if (this.receiveLoopTask != null)
            {
                await this.receiveLoopTask;
            }

            try
            {
                this.serialPort?.Close();
            }
            catch (UnauthorizedAccessException)
            {
            }

            this.serialPort = null;

            this.State = PrinterState.Disconnected;
            this.Temperatures = null;

            this.logger.LogDebug("Disconnected successfully");
        }

        /// <inheritdoc/>
        public Task ExecutePrintAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Ready)
            {
                throw new InvalidOperationException("Printer isn't ready");
            }

            CancellationTokenSource cancellationTokenSource = new();

            this.printCancellationTokenSource = cancellationTokenSource;
            this.State = PrinterState.Printing;

            Task task = Task.Run(() => this.RunPrintAsync(stream, cancellationTokenSource.Token), cancellationToken);

            this.printTask = task.ContinueWith((t) => this.HandleTaskCompletedAsync(t, "Print"), cancellationToken);

            return task;
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
        public async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            if (this.State != PrinterState.Printing && this.State != PrinterState.Pausing && this.State != PrinterState.Paused)
            {
                throw new InvalidOperationException("Not printing");
            }

            this.State = PrinterState.Canceling;

            this.printCancellationTokenSource?.Cancel();

            if (this.printTask != null)
            {
                // TODO: add cancel G-code here
                await this.printTask;
            }

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
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

                this.serialCommandManager?.Dispose();
                this.serialCommandManager = null;

                if (this.backgroundTaskCancellationTokenSource != null)
                {
                    this.backgroundTaskCancellationTokenSource.Cancel();
                    this.backgroundTaskCancellationTokenSource.Dispose();
                    this.backgroundTaskCancellationTokenSource = null;
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
            return new TemperatureSensor(sensor, currentTemperature, targetTemperature);
        }

        private async Task<MarlinSettings> GetSettingsAsync(SerialCommandManager serialCommandManager, CancellationToken cancellationToken)
        {
            this.logger.LogTrace("Getting settings");

            Task sendCommandTask = serialCommandManager.SendCommandAsync(ReportSettingsCommand, cancellationToken);
            MarlinMessage message;
            MarlinSettings settings = new();

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                message = await serialCommandManager.ReceiveLineAsync(cancellationToken);

                if (message.Type != MarlinMessageType.Message)
                {
                    continue;
                }

                string line = message.Content.Trim();

                if (line.StartsWith(';'))
                {
                    continue;
                }

                int index = line.IndexOf(';');

                if (index >= 0)
                {
                    line = line[..index].Trim();
                }

                while (line.StartsWith("echo:"))
                {
                    line = line[5..].Trim();
                }

                int spaceIndex = line.IndexOf(' ');

                // we're only interested in G-code commands with arguments
                if (spaceIndex == -1)
                {
                    continue;
                }

                string command = line[..spaceIndex];

                Match match;

                switch (command)
                {
                    case "M201":
                        match = PerAxisCommandRegex.Match(line);

                        if (!match.Success)
                        {
                            continue;
                        }

                        settings.MaximumAcceleration = new PerAxis(
                            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));

                        break;

                    case "M203":
                        match = PerAxisCommandRegex.Match(line);

                        if (!match.Success)
                        {
                            continue;
                        }

                        settings.MaximumFeedrates = new PerAxis(
                            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture));

                        break;

                    case "M204":
                        match = AccelerationCommandRegex.Match(line);

                        if (!match.Success)
                        {
                            continue;
                        }

                        settings.Acceleration = new Acceleration(
                            double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));

                        break;
                }
            }
            while (message.Type != MarlinMessageType.CommandAcknowledgement && !sendCommandTask.IsCompleted);

            await sendCommandTask;

            return settings;
        }

        private async Task<MarlinFirmwareInfo> GetFirmwareInfoAsync(SerialCommandManager serialCommandManager, CancellationToken cancellationToken)
        {
            this.logger.LogTrace("Getting firmware information");

            Task sendCommandTask = serialCommandManager.SendCommandAsync(FirmwareInfoCommand, cancellationToken);
            MarlinMessage message;
            MarlinFirmwareInfo info = new();

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                message = await serialCommandManager.ReceiveLineAsync(cancellationToken);

                if (message.Type != MarlinMessageType.Message)
                {
                    continue;
                }

                if (message.Content.StartsWith("FIRMWARE_NAME"))
                {
                    Match match = FirmwareInfoRegex.Match(message.Content);

                    if (!match.Success)
                    {
                        continue;
                    }

                    info.Name = match.Groups["firmware_name"].Value;
                    info.ExtruderCount = int.Parse(match.Groups["extruder_count"].Value);
                    info.Uuid = match.Groups["uuid"].Value;
                }
                else if (message.Content.StartsWith("Cap:"))
                {
                    switch (message.Content[4..].Trim())
                    {
                        case "AUTOREPORT_TEMP:1":
                            info.CanAutoReportTemperatures = true;
                            break;

                        case "EMERGENCY_PARSER:1":
                            info.HasEmergencyParser = true;
                            break;
                    }
                }
            }
            while (message.Type != MarlinMessageType.CommandAcknowledgement && !sendCommandTask.IsCompleted);

            await sendCommandTask;

            return info;
        }

        private async Task RunPrintAsync(Stream stream, CancellationToken cancellationToken)
        {
            // StreamReader takes care of closing the stream properly
            using (StreamReader streamReader = new(stream))
            {
                while (!streamReader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? line = await streamReader.ReadLineAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    await this.SendCommandAsync(line, cancellationToken).ConfigureAwait(false);
                }
            }

            // TODO: add completion G-code here
            this.State = PrinterState.Ready;
        }

        private async Task HandleTaskCompletedAsync(Task task, string name)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogDebug("{name} task completed", name);
            }
            else if (task.IsCanceled)
            {
                this.logger.LogDebug("{name} task canceled", name);
            }
            else if (task.IsFaulted)
            {
                // TODO: report to server
                // TODO: if a print is running, mark it as errored
                this.logger.LogError("{name} task errored", name);
                this.logger.LogError("{exception}", task.Exception!.ToString());

                await this.DisconnectAsync(CancellationToken.None);
            }
        }

        private async Task TemperaturePolling(CancellationToken cancellationToken)
        {
            while (this.serialCommandManager != null)
            {
                await this.serialCommandManager.SendCommandAsync(ReportTemperaturesCommand, cancellationToken).ConfigureAwait(false);
                await Task.Delay(TemperatureReportingIntervalSeconds * 1000, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            while (this.serialCommandManager != null)
            {
                (string content, MarlinMessageType type) = await this.serialCommandManager.ReceiveLineAsync(cancellationToken).ConfigureAwait(false);

                if (type != MarlinMessageType.Message)
                {
                    continue;
                }

                this.HandleLine(content);
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
