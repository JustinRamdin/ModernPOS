namespace Pos.Domain.Entities;

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "";         // e.g. "SaleCreated"
    public string PayloadJson { get; set; } = "";  // serialized DTO
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAtUtc { get; set; }
}
