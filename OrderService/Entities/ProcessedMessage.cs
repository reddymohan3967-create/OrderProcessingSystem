namespace OrderService.Entities;

public class ProcessedMessage
{
    public Guid Id { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
