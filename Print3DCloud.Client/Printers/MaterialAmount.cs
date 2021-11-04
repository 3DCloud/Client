namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents an amount of material used by a print.
    /// </summary>
    /// <param name="Amount">The amount of material.</param>
    /// <param name="Type">The amount type.</param>
    public record MaterialAmount(double Amount, MaterialAmountType Type);
}