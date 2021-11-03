namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// A time estimate.
    /// </summary>
    /// <param name="TimeRemaining">The time remaining, in seconds.</param>
    /// <param name="Progress">The total estimated progress, from 0 to 1.</param>
    public record TimeEstimate(int TimeRemaining, double Progress);
}