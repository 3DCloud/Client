using System;

namespace Print3DCloud.Client.Models
{
    internal record Device(int Id, string Name, string Path, string? SerialNumber, DateTime LastSeen, Guid ClientId, Client Client, DateTime CreatedAt, DateTime UpdatedAt);
}
