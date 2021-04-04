using System.Collections.Generic;

namespace Print3DCloud.Client.Printers
{
    /// <summary>
    /// Represents the state of a printer at a given moment.
    /// </summary>
    internal record PrinterState(bool IsConnected, bool IsPrinting, TemperatureSensor ActiveHotendTemperature, IEnumerable<TemperatureSensor> HotendTemperatures, TemperatureSensor? BuildPlateTemperature);
}
