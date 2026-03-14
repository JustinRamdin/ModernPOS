using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;

namespace Pos.Server.Data;

public static class Seeder
{
    public static async Task SeedAsync(PosDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureSqliteCompatibilityAsync(db);

        if (await db.Products.AnyAsync())
            return;

        db.Products.AddRange(
            new Product { Sku = "SKU-001", Name = "Bottled Water 500ml", Price = 8.00m },
            new Product { Sku = "SKU-002", Name = "Soft Drink 500ml", Price = 12.00m },
            new Product { Sku = "SKU-003", Name = "Snack Chips", Price = 10.00m });

        await db.SaveChangesAsync();
    }
     private static async Task EnsureSqliteCompatibilityAsync(PosDbContext db)
    {
        if (!db.Database.IsSqlite())
            return;

        const string missingDisplayNameColumnQuery = """
            SELECT COUNT(1)
            FROM pragma_table_info('UserAccounts')
            WHERE name = 'DisplayName';
            """;

        var hasDisplayNameColumn = await db.Database.SqlQueryRaw<int>(missingDisplayNameColumnQuery).SingleAsync() == 1;
        if (hasDisplayNameColumn)
            return;

        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserAccounts\" ADD COLUMN \"DisplayName\" TEXT NOT NULL DEFAULT ''; ");
    }
}
