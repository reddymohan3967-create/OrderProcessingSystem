using System;

namespace OrderService.Entities;

public class PendingWork
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
}
