﻿namespace Print3DCloud.Client.Printers
{
    internal record PrinterStateWithTemperatures(PrinterState PrinterState, PrinterTemperatures? Temperatures);
}
