using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;

namespace Pos.Local.Services;

public class InventoryRepository
{
    private readonly PosLocalDbContext _db;

    public InventoryRepository(PosLocalDbContext db)
    {
        _db = db;
    }

    public async Task<List<InventoryItemDto>> GetInventoryAsync(
        string locationCode = "DEFAULT",
        CancellationToken ct = default)
    {
        var query =
            from p in _db.Products
            join i in _db.Inventory
                on new { ProductId = p.Id, LocationCode = locationCode }
                equals new { i.ProductId, i.LocationCode }
                into inv
            from i in inv.DefaultIfEmpty()
            where p.DeletedAtUtc == null && p.IsActive
            orderby p.Name
            select new InventoryItemDto
            {
                ProductId = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                OnHand = i != null ? i.OnHand : 0m
            };

        return await query.AsNoTracking().ToListAsync(ct);
    }
}

public sealed class InventoryItemDto
{
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal OnHand { get; init; }
}
