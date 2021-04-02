using System;

namespace Print3DCloud.Client.Models
{
    internal record Printer(string DeviceId, DateTime CreatedAt, DateTime UpdatedAt, long ClientId, long PrinterDefinitionId, PrinterDefinition? PrinterDefinition);
}
