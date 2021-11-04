namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Contains G-code printer settings.
    /// </summary>
    /// <param name="StartGCode">The G-code to run before a print starts.</param>
    /// <param name="EndGCode">The G-code to run after a print ends.</param>
    /// <param name="AbortGCode">The G-code to run when a print is aborted.</param>
    public record GCodeSettings(string? StartGCode = null, string? EndGCode = null, string? AbortGCode = null);
}