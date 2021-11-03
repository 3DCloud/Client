namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a progress step at a specific position in a print file.
    /// </summary>
    /// <param name="BytePosition">The position at which the progress is set.</param>
    /// <param name="TimeRemaining">The remaining time at the specified position.</param>
    public record ProgressTimeStep(long BytePosition, int TimeRemaining);
}