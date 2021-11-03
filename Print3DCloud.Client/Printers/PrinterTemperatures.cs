using System.Collections.Generic;

namespace Print3DCloud.Client.Printers
{
    internal record PrinterTemperatures(IEnumerable<TemperatureSensor> HotendTemperatures, TemperatureSensor? BedTemperature);
}
