using System;

namespace Print3DCloud.Client.Models
{
    internal record Device(int Id, string DeviceName, string HardwareIdentifier, bool IsPortableHardwareIdentifier, DateTime LastSeen, Guid ClientId, Client Client, DateTime CreatedAt, DateTime UpdatedAt);
}
