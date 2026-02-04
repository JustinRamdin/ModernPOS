namespace Pos.Local.Entities;

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // e.g. "sale", "customer", "inventory"
    public string EntityType { get; set; } = "";

    public Guid EntityId { get; set; }

    // "UPSERT" | "DELETE"
    public string Operation { get; set; } = "UPSERT";

    // JSON payload for SyncAgent to send to PostgREST
    public string PayloadJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SentAtUtc { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
