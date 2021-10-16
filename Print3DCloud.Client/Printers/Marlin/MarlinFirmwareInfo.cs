namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Contains various details about the running version of Marlin.
    /// </summary>
    public record MarlinFirmwareInfo
    {
        /// <summary>
        /// Gets or sets the name and version of the firmware.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the number of extruders.
        /// </summary>
        public int ExtruderCount { get; set; }

        /// <summary>
        /// Gets or sets the machine's unique ID.
        /// </summary>
        public string? Uuid { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the printer supports automatic temperature reporting or not.
        /// </summary>
        public bool CanAutoReportTemperatures { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the printer has an emergency parser or not.
        /// </summary>
        public bool HasEmergencyParser { get; set; }
    }
}