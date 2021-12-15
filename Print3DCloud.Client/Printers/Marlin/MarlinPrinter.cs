using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ActionCableSharp;
using Microsoft.Extensions.Logging;
using Print3DCloud.Client.ActionCable;

namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Printer driver for printers using Marlin (or derived) firmware that send G-code via serial.
    /// </summary>
    internal class MarlinPrinter : Printer, IGCodePrinter, IUltiGCodePrinter
    {
        /// <summary>
        /// Serial printer driver ID as defined by the back-end.
        /// </summary>
        public const string DriverId = "marlin";

        /// <summary>
        /// Directory in which temporary files are stored.
        /// </summary>
        public static readonly string TemporaryFileDirectory = Path.Join(Directory.GetCurrentDirectory(), "tmp");

        private const string UltiGCodeFlavor = "UltiGCode";
        private const string ReportTemperaturesCommand = "M105";
        private const string FirmwareInfoCommand = "M115";
        private const string ReportSettingsCommand = "M503";
        private const string AutomaticTemperatureReportingCommand = "M155 S{0}";
        private const int TemperatureReportingIntervalSeconds = 1;
        private const int MaxConnectRetries = 5;
        private const int RetryConnectDelayMs = 1000;

        private static readonly string[] HeatingCommands = { "M109", "M190" };

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
        private int currentExtruder;
        private bool isUltiGCodePrint;

        private Task? temperaturePollingTask;
        private Task? receiveLoopTask;
        private Task? printTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="MarlinPrinter"/> class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="subscription">The subscription to use when communicating with the server.</param>
        /// <param name="serialPortFactory">The factory to use when creating a stream for communicating with the printer.</param>
        /// <param name="portName">Name of the serial port to which this printer should connect.</param>
        /// <param name="baudRate">The baud rate to be used for serial communication.</param>
        public MarlinPrinter(ILogger<MarlinPrinter> logger, IActionCableSubscription subscription, ISerialPortFactory serialPortFactory, string portName, int baudRate = 250_000)
            : base(logger, subscription)
        {
            this.serialPortFactory = serialPortFactory;
            this.logger = logger;
            this.portName = portName;
            this.baudRate = baudRate;
        }

        /// <summary>
        /// Gets the printer's settings.
        /// </summary>
        public MarlinSettings? Settings { get; private set; }

        /// <inheritdoc/>
        public GCodeSettings? GCodeSettings { get; set; }

        /// <inheritdoc/>
        public UltiGCodeSettings?[] UltiGCodeSettings { get; set; } = Array.Empty<UltiGCodeSettings>();

        /// <inheritdoc/>
        public override async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (this.IsInState(PrinterState.Ready))
            {
                throw new InvalidOperationException("Printer is already connected");
            }

            // we control the printer so we know that reconnecting means an ongoing print is no longer running
            await this.SendPrintEvent(PrintEventType.Errored, CancellationToken.None);

            this.State = PrinterState.Connecting;
            int tries = 0;

            while (true)
            {
                try
                {
                    this.logger.LogInformation("Connecting to Marlin printer at port '{PortName}'...", this.portName);

                    this.serialPort = this.serialPortFactory.CreateSerialPort(this.portName, this.baudRate);

                    // ensure the port is actually closed
                    // it sometimes stays open if printer isn't disconnected gracefully on Linux
                    this.serialPort.Close();

                    // OctoPrint needs to do this as well, not sure why it's necessary
                    if (OperatingSystem.IsLinux() && File.Exists("/etc/debian_version"))
                    {
                        this.serialPort.Parity = Parity.Odd;
                        this.serialPort.Open();
                        this.serialPort.Close();
                        this.serialPort.Parity = Parity.None;
                    }

                    this.serialPort.Open();

                    this.serialPort.DiscardInBuffer();
                    this.serialPort.DiscardOutBuffer();

                    this.serialCommandManager = new SerialCommandManager(this.logger, this.serialPort.BaseStream, Encoding.UTF8, "\n");

                    // TODO: WaitForStartupAsync can block even once the cancellation token is signaled, this isn't ideal
                    TaskCompletionSource tcs = new();
                    cancellationToken.Register(() => tcs.TrySetCanceled());

                    await Task.WhenAny(
                        this.serialCommandManager.WaitForStartupAsync(cancellationToken),
                        tcs.Task);

                    cancellationToken.ThrowIfCancellationRequested();

                    break;
                }
                catch (OperationCanceledException)
                {
                    await this.DisconnectAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    this.logger.LogError("Failed to connect to printer\n{Exception}", ex);

                    if (++tries >= MaxConnectRetries)
                    {
                        await this.DisconnectAsync(CancellationToken.None);
                        throw;
                    }

                    await Task.Delay(RetryConnectDelayMs, cancellationToken);
                }
            }

            this.backgroundTaskCancellationTokenSource = new CancellationTokenSource();

            try
            {
                this.Settings = await this.GetSettingsAsync(this.serialCommandManager, cancellationToken);
                MarlinFirmwareInfo firmwareInfo = await this.GetFirmwareInfoAsync(this.serialCommandManager, cancellationToken);

                this.receiveLoopTask = Task.Run(() => this.ReceiveLoop(this.backgroundTaskCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Receive loop"), CancellationToken.None);

                if (firmwareInfo.CanAutoReportTemperatures)
                {
                    await this.SendCommandAsync(string.Format(AutomaticTemperatureReportingCommand, TemperatureReportingIntervalSeconds), cancellationToken);
                }
                else
                {
                    this.temperaturePollingTask = Task.Run(() => this.TemperaturePolling(this.backgroundTaskCancellationTokenSource.Token), cancellationToken).ContinueWith(t => this.HandleTaskCompletedAsync(t, "Temperature polling"), CancellationToken.None);
                }
            }
            catch (Exception)
            {
                await this.DisconnectAsync(CancellationToken.None);
                throw;
            }

            this.logger.LogInformation("Connected successfully");

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        public override Task SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (this.serialCommandManager == null)
            {
                throw new InvalidOperationException("Printer isn't connected");
            }

            return this.serialCommandManager.SendCommandAsync(command, cancellationToken);
        }

        /// <summary>
        /// Send a block of commands as an asynchronous task.
        /// </summary>
        /// <param name="block">Block of commands to send.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once the command block has been sent.</returns>
        public async Task SendCommandBlockAsync(string block, CancellationToken cancellationToken)
        {
            if (this.serialCommandManager == null)
            {
                throw new InvalidOperationException("Printer isn't connected");
            }

            foreach (string command in block.Split('\n'))
            {
                await this.serialCommandManager.SendCommandAsync(command, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public override async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (this.IsInState(PrinterState.Disconnecting) || this.IsInState(PrinterState.Disconnected))
            {
                return;
            }

            this.Progress = null;
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
        public override async Task StartPrintAsync(Stream stream, CancellationToken cancellationToken)
        {
            this.State = PrinterState.Downloading;

            string path = Path.Join(TemporaryFileDirectory, Guid.NewGuid().ToString());

            Directory.CreateDirectory(TemporaryFileDirectory);

            this.logger.LogInformation("Saving print file to '{FilePath}'", path);

            await using (FileStream writeFileStream = new(path, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(writeFileStream, cancellationToken);
            }

            CancellationTokenSource cancellationTokenSource = new();

            this.printCancellationTokenSource = cancellationTokenSource;
            this.State = PrinterState.Printing;

            FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);

            await this.SendPrintEvent(PrintEventType.Running, CancellationToken.None);

            this.printTask =
                Task.Run(() => this.RunPrintAsync(fileStream, cancellationTokenSource.Token), cancellationToken)
                    .ContinueWith(t => this.HandlePrintTaskCompletedAsync(t, path));
        }

        /// <inheritdoc/>
        public override async Task AbortPrintAsync(CancellationToken cancellationToken)
        {
            if (!this.IsInState(PrinterState.Printing))
            {
                throw new InvalidOperationException("Not printing");
            }

            this.State = PrinterState.Canceling;

            this.printCancellationTokenSource?.Cancel();

            if (this.printTask != null)
            {
                await this.printTask;
            }

            this.Progress = null;

            if (this.isUltiGCodePrint)
            {
                await this.ExecuteUltiGCodePostambleAsync(cancellationToken);
            }

            if (this.GCodeSettings?.CancelGCode != null)
            {
                await this.SendCommandBlockAsync(this.GCodeSettings.CancelGCode, cancellationToken);
            }

            this.State = PrinterState.Ready;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Progress = null;
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
            if (this.GCodeSettings?.StartGCode != null)
            {
                await this.SendCommandBlockAsync(this.GCodeSettings.StartGCode, cancellationToken);
            }

            // GCodeFile takes care of closing the stream properly
            using (GCodeFile gCodeFile = new(stream))
            {
                await gCodeFile.PreprocessAsync(cancellationToken);

                GcodeProgressCalculator progressCalculator = new(gCodeFile.TotalTime, gCodeFile.ProgressSteps);

                int maxFanSpeedPercent = 100;

                this.isUltiGCodePrint = gCodeFile.Flavor == UltiGCodeFlavor;

                if (this.isUltiGCodePrint)
                {
                    this.logger.LogInformation("UltiGCode detected!");
                    await this.ExecuteUltiGCodePreambleAsync(gCodeFile, cancellationToken);
                    maxFanSpeedPercent = this.UltiGCodeSettings.Length > 0
                        ? this.UltiGCodeSettings.Max(s => s?.FanSpeed ?? 0)
                        : 0;
                }

                Stopwatch? stopwatch = null;

                await foreach (string command in gCodeFile)
                {
                    // only start stopwatch once we reach the first step
                    if (stopwatch == null && gCodeFile.ProgressSteps.Count > 0 && stream.Position > gCodeFile.ProgressSteps[0].BytePosition)
                    {
                        stopwatch = Stopwatch.StartNew();
                    }

                    string commandToSend = command;

                    cancellationToken.ThrowIfCancellationRequested();

                    string code = command.Contains(' ') ? command[..command.IndexOf(' ')] : command;

                    if (HeatingCommands.Contains(code))
                    {
                        this.State = PrinterState.Heating;
                    }
                    else
                    {
                        this.State = PrinterState.Printing;
                    }

                    if (command.StartsWith('T'))
                    {
                        this.currentExtruder = int.Parse(command[1..], CultureInfo.InvariantCulture);
                    }

                    if (maxFanSpeedPercent != 100 && command.StartsWith("M106") && command.Contains('S'))
                    {
                        int start = command.IndexOf('S') + 1;
                        int end = command.IndexOf(' ', start);

                        if (end == -1)
                        {
                            end = command.Length;
                        }

                        string fanSpeedStr = command[start..end];
                        int fanSpeed = int.Parse(fanSpeedStr);
                        int adjustedFanSpeed = (int)Math.Round(Math.Clamp(fanSpeed * maxFanSpeedPercent / 100d, 0, 250));

                        commandToSend = FormattableString.Invariant($"M106 S{adjustedFanSpeed}");
                    }

                    await this.SendCommandAsync(commandToSend, cancellationToken).ConfigureAwait(false);

                    // Stream.Position doesn't return the actual current position since
                    // StreamReader buffers in chunks, but it's good enough for our purposes.
                    TimeEstimate estimate = progressCalculator.GetEstimate(stopwatch?.Elapsed.TotalSeconds ?? 0, stream.Position);
                    this.TimeRemaining = estimate.TimeRemaining;
                    this.Progress = estimate.Progress;
                }

                if (this.isUltiGCodePrint)
                {
                    await this.ExecuteUltiGCodePostambleAsync(cancellationToken);
                }
            }

            if (this.GCodeSettings?.EndGCode != null)
            {
                await this.SendCommandBlockAsync(this.GCodeSettings.EndGCode, cancellationToken);
            }

            this.Progress = null;
            this.State = PrinterState.Ready;
        }

        /// <summary>
        /// Executes startup G-code when using UltiGCode.
        /// Based on what the firmware does here:
        /// - https://github.com/Ultimaker/Ultimaker2Marlin/blob/8698fb6ad43490ce452d044330f9ca3a17027dfc/Marlin/UltiLCD2_menu_print.cpp#L406-L443
        /// - https://github.com/Ultimaker/Ultimaker2Marlin/blob/8698fb6ad43490ce452d044330f9ca3a17027dfc/Marlin/UltiLCD2_menu_print.cpp#L464-L482
        /// - https://github.com/Ultimaker/Ultimaker2Marlin/blob/8698fb6ad43490ce452d044330f9ca3a17027dfc/Marlin/UltiLCD2_menu_print.cpp#L125-L166.
        /// </summary>
        /// <param name="gCodeFile">The <see cref="GCodeFile"/>.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once all preamble commands have been sent.</returns>
        private async Task ExecuteUltiGCodePreambleAsync(GCodeFile gCodeFile, CancellationToken cancellationToken)
        {
            if (this.UltiGCodeSettings.Length == 0)
            {
                return;
            }

            int buildPlateTemperature = this.UltiGCodeSettings.Max(s => s?.BuildPlateTemperature ?? 0);

            await this.SendCommandAsync("G28", cancellationToken); // home all axes
            await this.SendCommandAsync("G1 F12000 X5 Y10", cancellationToken); // move to front left corner

            this.State = PrinterState.Heating;

            // wait for build plate to heat up
            await this.SendCommandAsync(FormattableString.Invariant($"M190 S{buildPlateTemperature}"), cancellationToken);

            for (int i = 0; i < this.UltiGCodeSettings.Length; i++)
            {
                // skip if extruder isn't used
                if (gCodeFile.MaterialAmounts.Count <= i || gCodeFile.MaterialAmounts[i].Amount <= 0)
                {
                    continue;
                }

                UltiGCodeSettings? settings = this.UltiGCodeSettings[i];

                if (settings == null)
                {
                    continue;
                }

                await this.SendCommandAsync(FormattableString.Invariant($"T{i}"), cancellationToken); // switch to extruder at index i
                await this.SendCommandAsync(FormattableString.Invariant($"M200 D{settings.FilamentDiameter}"), cancellationToken); // set filament diameter
                await this.SendCommandAsync(FormattableString.Invariant($"M207 S{settings.RetractionLength} F{settings.RetractionSpeed * 60}"), cancellationToken); // set retraction length and speed
                await this.SendCommandAsync(FormattableString.Invariant($"M109 S{settings.HotendTemperature}"), cancellationToken); // wait for hotend to heat up
            }

            await this.SendCommandAsync("G0 Z2", cancellationToken); // move build plate up

            for (int i = 0; i < this.UltiGCodeSettings.Length; i++)
            {
                UltiGCodeSettings? settings = this.UltiGCodeSettings[i];

                // don't prime if extruder isn't used
                if (gCodeFile.MaterialAmounts.Count <= i || gCodeFile.MaterialAmounts[i].Amount <= 0 || settings == null)
                {
                    continue;
                }

                double volumeToFilamentLength = 1 / (Math.PI * Math.Pow(settings.FilamentDiameter / 2, 2));

                await this.SendCommandAsync(FormattableString.Invariant($"T{i}"), cancellationToken);
                await this.SendCommandAsync("M83", cancellationToken); // relative extruder positioning
                await this.SendCommandAsync(FormattableString.Invariant($"G1 E{settings.EndOfPrintRetractionLength / volumeToFilamentLength} F{settings.RetractionSpeed * 60}"), cancellationToken);
                await this.SendCommandAsync(FormattableString.Invariant($"G1 E50 F{20 * volumeToFilamentLength * 60}"), cancellationToken);
                await this.SendCommandAsync(FormattableString.Invariant($"G1 Z5 E5 F{20 * volumeToFilamentLength * 60}"), cancellationToken);
                await this.SendCommandAsync(FormattableString.Invariant($"G1 E5 F{20 * volumeToFilamentLength * 60}"), cancellationToken);

                // retract more for non-0 extruder
                if (i > 0)
                {
                    await this.SendCommandAsync(FormattableString.Invariant($"G1 E-{settings.EndOfPrintRetractionLength / volumeToFilamentLength} F{settings.RetractionSpeed * 60}"), cancellationToken);
                }
            }

            await this.SendCommandAsync("M209 S0", cancellationToken); // disable auto-retract (and reset retraction state)
            await this.SendCommandAsync("G90", cancellationToken);
            await this.SendCommandAsync("G92 E0", cancellationToken);
        }

        /// <summary>
        /// Executes end of print G-code when using UltiGCode.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> used to propagate notification that the operation should be canceled.</param>
        /// <returns>A <see cref="Task"/> that completes once all postamble commands have been sent.</returns>
        private async Task ExecuteUltiGCodePostambleAsync(CancellationToken cancellationToken)
        {
            if (this.UltiGCodeSettings.Length == 0 ||
                this.currentExtruder >= this.UltiGCodeSettings.Length)
            {
                return;
            }

            UltiGCodeSettings? settings = this.UltiGCodeSettings[this.currentExtruder];

            if (settings == null)
            {
                return;
            }

            double volumeToFilamentLength = 1 / (Math.PI * Math.Pow(settings.FilamentDiameter / 2, 2));

            await this.SendCommandAsync("M104 S0", cancellationToken); // turn off extruder header
            await this.SendCommandAsync("M140 S0", cancellationToken); // turn off build plate header
            await this.SendCommandAsync("M107", cancellationToken); // turn off fans
            await this.SendCommandAsync("G91", cancellationToken); // relative positioning
            await this.SendCommandAsync(FormattableString.Invariant($"G1 E-{settings.EndOfPrintRetractionLength / volumeToFilamentLength} F{settings.RetractionSpeed * 60}"), cancellationToken);
            await this.SendCommandAsync("G28", cancellationToken); // home all axes
            await this.SendCommandAsync("M84", cancellationToken); // disable steppers
            await this.SendCommandAsync("G90", cancellationToken); // absolute positioning
        }

        private async Task HandlePrintTaskCompletedAsync(Task task, string temporaryFilePath)
        {
            await this.HandleTaskCompletedAsync(task, "Print");

            if (task.IsCompletedSuccessfully)
            {
                await this.SendPrintEvent(PrintEventType.Success, CancellationToken.None);
            }
            else
            {
                await this.SendPrintEvent(PrintEventType.Errored, CancellationToken.None);
            }

            try
            {
                File.Delete(temporaryFilePath);
                this.logger.LogTrace("Deleted '{FilePath}'", temporaryFilePath);
            }
            catch (IOException ex)
            {
                this.logger.LogError(ex, "Failed to delete temporary file '{FilePath}'", temporaryFilePath);
            }
        }

        private async Task HandleTaskCompletedAsync(Task task, string name)
        {
            if (task.IsCompletedSuccessfully)
            {
                this.logger.LogDebug("{Name} task completed", name);
            }
            else if (task.IsCanceled)
            {
                this.logger.LogDebug("{Name} task canceled", name);
            }
            else if (task.IsFaulted)
            {
                this.logger.LogError(task.Exception, "{Name} task errored", name);

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

                // if temperatures aren't auto-reported, they are reported along with "ok"
                if (type != MarlinMessageType.Message && type != MarlinMessageType.CommandAcknowledgement)
                {
                    continue;
                }

                this.HandleLine(content);
            }
        }

        private void HandleLine(string line)
        {
            Dictionary<string, TemperatureSensor> sensors = new();
            Match match = TemperaturesRegex.Match(line);

            if (!match.Success)
            {
                return;
            }

            do
            {
                TemperatureSensor sensor = GetSensorFromMatch(match);

                if (sensors.ContainsKey(sensor.Name))
                {
                    sensors[sensor.Name] = sensor;
                }
                else
                {
                    sensors.Add(sensor.Name, sensor);
                }

                match = match.NextMatch();
            }
            while (match.Success);

            sensors.TryGetValue("B", out TemperatureSensor? bedTemperature);

            this.Temperatures = new PrinterTemperatures(sensors.Values.Where(s => s.Name[0] == 'T'), bedTemperature);
        }
    }
}
