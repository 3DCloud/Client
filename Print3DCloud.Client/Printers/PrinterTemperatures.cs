using System.Collections.Generic;

namespace Print3DCloud.Client.Printers
{
    internal record PrinterTemperatures(TemperatureSensor ActiveHotendTemperature, IEnumerable<TemperatureSensor> HotendTemperatures, TemperatureSensor? BedTemperature);
}
