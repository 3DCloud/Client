namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents a printer's temperature sensor (hotend or build plate).
    /// </summary>
    public record TemperatureSensor(string Name, double Current, double Target);
}
