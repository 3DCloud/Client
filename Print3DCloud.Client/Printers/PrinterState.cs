namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents the state of a printer at a given moment.
    /// </summary>
    internal readonly struct PrinterState
    {
        /// <summary>
        /// Gets a value indicating whether the printer is connected or not.
        /// </summary>
        public bool IsConnected { get; init; }

        /// <summary>
        /// Gets a value indicating whether the printer is printing or not.
        /// </summary>
        public bool IsPrinting { get; init; }

        /// <summary>
        /// Gets the temperature of the active hotend.
        /// </summary>
        public TemperatureSensor ActiveHotendTemperature { get; init; }

        /// <summary>
        /// Gets the temperature(s) of the printer's hotend(s).
        /// </summary>
        public TemperatureSensor[] HotendTemperatures { get; init; }

        /// <summary>
        /// Gets the temperature of the bed. Set to null if the printer does not have a heated build plate.
        /// </summary>
        public TemperatureSensor? BuildPlateTemperature { get; init; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{{ IsConnected = {this.IsConnected}, IsPrinting = {this.IsPrinting}, HotendTemperatures = [{string.Join(", ", this.HotendTemperatures)}], BuildPlateTemperature = {this.BuildPlateTemperature?.ToString() ?? "N/A"} }}";
        }
    }
}
