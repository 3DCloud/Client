namespace Print3DCloud.Client.Printers.Marlin
{
    /// <summary>
    /// Contains information stored by Marlin in SRAM.
    /// </summary>
    public record MarlinSettings
    {
        /// <summary>
        /// Gets or sets the maximum feedrate (mm/s) per axis.
        /// </summary>
        public PerAxis? MaximumFeedrates { get; set; }

        /// <summary>
        /// Gets or sets the maximum acceleration (mm/s²) per axis.
        /// </summary>
        public PerAxis? MaximumAcceleration { get; set; }

        /// <summary>
        /// Gets or sets the maximum acceleration (mm/s²) per movement type.
        /// </summary>
        public Acceleration? Acceleration { get; set; }
    }

    /// <summary>
    /// Stores values specific to each of the printer's axes (X/Y/Z/E).
    /// </summary>
    /// <param name="X">The value for the X axis.</param>
    /// <param name="Y">The value for the Y axis.</param>
    /// <param name="Z">The value for the Z axis.</param>
    /// <param name="E">The value for the E (extruder) axis/axes.</param>
    public record PerAxis(double X, double Y, double Z, double E);

    /// <summary>
    /// Stores acceleration for printing, retraction, and travel.
    /// </summary>
    /// <param name="Print">The acceleration when printing.</param>
    /// <param name="Retract">The acceleration when retracting.</param>
    /// <param name="Travel">The acceleration while travelling.</param>
    public record Acceleration(double Print, double Retract, double Travel);
}