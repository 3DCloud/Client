using System;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.Models
{
    internal record Printer(string Name, DateTime CreatedAt, DateTime UpdatedAt, long DeviceId, Device? Device, long PrinterDefinitionId, PrinterDefinition? PrinterDefinition);
}
