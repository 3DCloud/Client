using System;

namespace Print3DCloud.Client.Models
{
    internal record PrinterDefinition(string Name, string Driver, string? StartGcode, string EndGcode, string PauseGcode, string? ResumeGcode, DateTime CreatedAt, DateTime UpdatedAt);
}
