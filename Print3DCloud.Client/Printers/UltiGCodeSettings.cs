namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents settings used when UltiGCode is detected.
    /// </summary>
    /// <param name="MaterialName">Name of the material.</param>
    /// <param name="HotendTemperature">Target hotend temperature.</param>
    /// <param name="BuildPlateTemperature">Target build plate temperature.</param>
    /// <param name="RetractionLength">Distance to retract when G10/G11 is run.</param>
    /// <param name="EndOfPrintRetractionLength">Distance to retract when the print ends.</param>
    /// <param name="RetractionSpeed">Speed at which to retract.</param>
    /// <param name="FanSpeed">Maximum fan speed, in percent.</param>
    /// <param name="FlowRate">Material flow rate multiplier, in percent.</param>
    /// <param name="FilamentDiameter">Filament diameter, in mm. Standard values are 1.75 and 2.85.</param>
    public record UltiGCodeSettings(
        string? MaterialName,
        int HotendTemperature,
        int BuildPlateTemperature,
        double RetractionLength,
        double EndOfPrintRetractionLength,
        double RetractionSpeed,
        int FanSpeed,
        int FlowRate,
        double FilamentDiameter);
}