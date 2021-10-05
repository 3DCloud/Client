using System.Collections.Generic;

namespace Print3DCloud.Client.Printers
{
    internal record PrinterTemperatures(TemperatureSensor ActiveHotendTemperature, List<TemperatureSensor> HotendTemperatures, TemperatureSensor? BedTemperature);
}
