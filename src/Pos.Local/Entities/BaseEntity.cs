namespace Pos.Local.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Local timestamp for conflict resolution (V1 = last-write-wins)
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Optional soft delete
    public DateTime? DeletedAtUtc { get; set; }
}
