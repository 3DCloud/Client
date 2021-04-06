using System;

namespace Print3DCloud.Client.Models
{
    internal record Client(Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);
}
