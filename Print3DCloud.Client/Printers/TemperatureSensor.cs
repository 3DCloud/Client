namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a printer's temperature sensor (hotend or build plate).
    /// </summary>
    public readonly struct TemperatureSensor
    {
        /// <summary>
        /// Gets the name of the sensor, as reported by the firmware.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the current temperature reported by the sensor.
        /// </summary>
        public double Current { get; init; }

        /// <summary>
        /// Gets the target temperature reported by the firmware.
        /// </summary>
        public double Target { get; init; }
    }
}
