using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using Pos.Local.Entities;

namespace Pos.Local.Services;

public class ProductRepository
{
    private readonly PosLocalDbContext _db;

    public ProductRepository(PosLocalDbContext db)
    {
        _db = db;
    }

    public Task<List<Product>> GetAllActiveAsync(CancellationToken ct = default)
        => _db.Products
              .AsNoTracking()
              .Where(p => p.IsActive && p.DeletedAtUtc == null)
              .OrderBy(p => p.Name)
              .ToListAsync(ct);

    public Task<List<Product>> SearchAsync(string term, CancellationToken ct = default)
    {
        term = term.Trim();

        return _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAtUtc == null)
            .Where(p =>
                p.Name.Contains(term) ||
                p.Sku.Contains(term))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }
}
