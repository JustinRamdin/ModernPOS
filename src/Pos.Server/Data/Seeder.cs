using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Data;

namespace Pos.Server.Data;

public static class Seeder
{
    public static async Task SeedAsync(PosDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureSqliteCompatibilityAsync(db);
    }
    
    private static async Task EnsureSqliteCompatibilityAsync(PosDbContext db)
    {
        if (!db.Database.IsSqlite())
            return;

         await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Customers" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL DEFAULT '',
                "Phone" TEXT NOT NULL DEFAULT '',
                "Email" TEXT NOT NULL DEFAULT '',
                "Area" TEXT NOT NULL DEFAULT '',
                "Balance" TEXT NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAtUtc" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z',
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'
            );
            """);

        await EnsureColumnAsync(db, "Products", "Description", "ALTER TABLE \"Products\" ADD COLUMN \"Description\" TEXT NULL;");
        await EnsureColumnAsync(db, "Products", "CostPrice", "ALTER TABLE \"Products\" ADD COLUMN \"CostPrice\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "VatInclusive", "ALTER TABLE \"Products\" ADD COLUMN \"VatInclusive\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "IsLength", "ALTER TABLE \"Products\" ADD COLUMN \"IsLength\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "OnHand", "ALTER TABLE \"Products\" ADD COLUMN \"OnHand\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "OnHandInches", "ALTER TABLE \"Products\" ADD COLUMN \"OnHandInches\" INTEGER NOT NULL DEFAULT 0;");

        await EnsureColumnAsync(db, "UserAccounts", "DisplayName", "ALTER TABLE \"UserAccounts\" ADD COLUMN \"DisplayName\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "UserAccounts", "UpdatedAtUtc", "ALTER TABLE \"UserAccounts\" ADD COLUMN \"UpdatedAtUtc\" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'; ",
            "UPDATE \"UserAccounts\" SET \"UpdatedAtUtc\" = \"CreatedAtUtc\" WHERE \"UpdatedAtUtc\" = '0001-01-01T00:00:00.0000000Z';");
    }

    private static async Task EnsureColumnAsync(PosDbContext db, string tableName, string columnName, string alterSql, string? afterAddedSql = null)
    {
        var hasColumnQuery = $"""
            SELECT COUNT(1) AS Value
            FROM pragma_table_info('{tableName}')
            WHERE name = '{columnName}'
            """;

        var hasColumn = await db.Database.SqlQueryRaw<int>(hasColumnQuery).SingleAsync() == 1;
        if (hasColumn) return;

        await db.Database.ExecuteSqlRawAsync(alterSql);

        if (!string.IsNullOrWhiteSpace(afterAddedSql))
            await db.Database.ExecuteSqlRawAsync(afterAddedSql);
    }
}
