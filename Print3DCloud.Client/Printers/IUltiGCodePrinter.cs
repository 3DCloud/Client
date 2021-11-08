namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Interface for a printer that supports UltiGCode.
    /// </summary>
    public interface IUltiGCodePrinter
    {
        /// <summary>
        /// Gets or sets the settings to use when an UltiGCode print is started.
        /// </summary>
        public UltiGCodeSettings?[] UltiGCodeSettings { get; set; }
    }
}