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

        await EnsureColumnAsync(
            db,
            tableName: "UserAccounts",
            columnName: "DisplayName",
            alterSql: "ALTER TABLE \"UserAccounts\" ADD COLUMN \"DisplayName\" TEXT NOT NULL DEFAULT ''; ");

        await EnsureColumnAsync(
            db,
            tableName: "UserAccounts",
            columnName: "UpdatedAtUtc",
            alterSql: "ALTER TABLE \"UserAccounts\" ADD COLUMN \"UpdatedAtUtc\" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'; ",
            afterAddedSql: "UPDATE \"UserAccounts\" SET \"UpdatedAtUtc\" = \"CreatedAtUtc\" WHERE \"UpdatedAtUtc\" = '0001-01-01T00:00:00.0000000Z';");
    }

    private static async Task EnsureColumnAsync(
        PosDbContext db,
        string tableName,
        string columnName,
        string alterSql,
        string? afterAddedSql = null)
    {
        var hasColumnQuery = $"""
            SELECT COUNT(1) AS Value
            FROM pragma_table_info('{tableName}')
            WHERE name = '{columnName}'
            """;

        var hasColumn = await db.Database.SqlQueryRaw<int>(hasColumnQuery).SingleAsync() == 1;
        if (hasColumn)
            return;

        await db.Database.ExecuteSqlRawAsync(alterSql);

        if (!string.IsNullOrWhiteSpace(afterAddedSql))
            await db.Database.ExecuteSqlRawAsync(afterAddedSql);
    }
}
