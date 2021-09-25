namespace Print3DCloud.Client.Printers
{
    internal record PrinterStateWithTemperatures(string HardwareIdentifier, PrinterState PrinterState, PrinterTemperatures? Temperatures);
}
