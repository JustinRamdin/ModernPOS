using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using Pos.Local.Entities;

namespace Pos.Local.Services;

public class InventoryAdjustmentService
{
    private readonly PosLocalDbContext _db;

    public InventoryAdjustmentService(PosLocalDbContext db)
    {
        _db = db;
    }

    public async Task AdjustAsync(
        Guid productId,
        decimal delta,
        string reason,
        string locationCode = "DEFAULT",
        CancellationToken ct = default)
    {
        if (delta == 0) return;
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) reason = "Manual adjustment";

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var inv = await _db.Inventory
            .FirstOrDefaultAsync(x => x.ProductId == productId && x.LocationCode == locationCode, ct);

        if (inv == null)
        {
            inv = new InventoryBalance
            {
                ProductId = productId,
                LocationCode = locationCode,
                OnHand = 0m
            };
            _db.Inventory.Add(inv);
        }

        inv.OnHand = Math.Round(inv.OnHand + delta, 3);

        await _db.SaveChangesAsync(ct);

        var payload = new
        {
            type = "inventory_adjustment",
            product_id = productId,
            location_code = locationCode,
            delta = delta,
            reason = reason,
            occurred_at_utc = DateTime.UtcNow
        };

        _db.Outbox.Add(new OutboxEvent
        {
            EntityType = "inventory_adjustment",
            EntityId = Guid.NewGuid(),
            Operation = "UPSERT",
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
