﻿using System;

namespace Print3DCloud.Client.Models
{
    internal record PrinterDefinition(string Name, string? StartGcode, string EndGcode, string PauseGcode, string? ResumeGcode, string Driver, DateTime CreatedAt, DateTime UpdatedAt);
}
