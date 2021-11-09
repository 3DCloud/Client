using System;
using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.Models
{
    internal record PrinterDefinition(string Name, string Driver, DateTime CreatedAt, DateTime UpdatedAt, GCodeSettings GCodeSettings);
}
