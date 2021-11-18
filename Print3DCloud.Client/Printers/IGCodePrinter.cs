namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Interface for a printer that supports G-code.
    /// </summary>
    public interface IGCodePrinter
    {
        /// <summary>
        /// Gets or sets the printer's G-code settings.
        /// </summary>
        public GCodeSettings? GCodeSettings { get; set; }
    }
}