using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Data;

namespace Pos.Server.Data;

public static class Seeder
{
    public static async Task SeedAsync(PosDbContext db)
    {
        if (await HasMigrationsHistoryTableAsync(db))
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
        await EnsureSqliteCompatibilityAsync(db);
    }
    private static async Task<bool> HasMigrationsHistoryTableAsync(PosDbContext db)
    {
        if (!db.Database.IsSqlite())
            return true;

        var historyTableExists = await db.Database
            .SqlQueryRaw<int>("""
                SELECT COUNT(1) AS Value
                FROM sqlite_master
                WHERE type = 'table' AND name = '__EFMigrationsHistory'
                """)
            .SingleAsync();

        return historyTableExists == 1;
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

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CustomerPayments" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "Amount" TEXT NOT NULL DEFAULT 0,
                "Method" TEXT NOT NULL DEFAULT '',
                "ReferenceNo" TEXT NULL,
                "Note" TEXT NULL,
                "PaidAtUtc" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'
            );
            """);

        await EnsureColumnAsync(db, "Products", "Description", "ALTER TABLE \"Products\" ADD COLUMN \"Description\" TEXT NULL;");
        await EnsureColumnAsync(db, "Products", "CostPrice", "ALTER TABLE \"Products\" ADD COLUMN \"CostPrice\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "VatInclusive", "ALTER TABLE \"Products\" ADD COLUMN \"VatInclusive\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "ZeroRated", "ALTER TABLE \"Products\" ADD COLUMN \"ZeroRated\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "IsLength", "ALTER TABLE \"Products\" ADD COLUMN \"IsLength\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "OnHand", "ALTER TABLE \"Products\" ADD COLUMN \"OnHand\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "OnHandInches", "ALTER TABLE \"Products\" ADD COLUMN \"OnHandInches\" INTEGER NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Products", "InventoryBucket", "ALTER TABLE \"Products\" ADD COLUMN \"InventoryBucket\" INTEGER NOT NULL DEFAULT 1;");
        await EnsureColumnAsync(db, "Products", "Location", "ALTER TABLE \"Products\" ADD COLUMN \"Location\" TEXT NULL;");
        await EnsureColumnAsync(db, "Sales", "CustomerId", "ALTER TABLE \"Sales\" ADD COLUMN \"CustomerId\" TEXT NULL;");
        await EnsureColumnAsync(db, "Sales", "VatTotal", "ALTER TABLE \"Sales\" ADD COLUMN \"VatTotal\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "Sales", "ReceiptFooterOverride", "ALTER TABLE \"Sales\" ADD COLUMN \"ReceiptFooterOverride\" TEXT NULL;");
        await EnsureColumnAsync(db, "SaleLines", "VatTotal", "ALTER TABLE \"SaleLines\" ADD COLUMN \"VatTotal\" TEXT NOT NULL DEFAULT 0;");
        await EnsureColumnAsync(db, "SaleLines", "RefundedFromSaleLineId", "ALTER TABLE \"SaleLines\" ADD COLUMN \"RefundedFromSaleLineId\" TEXT NULL;");
        await EnsureColumnAsync(db, "Customers", "IsCompany", "ALTER TABLE \"Customers\" ADD COLUMN \"IsCompany\" INTEGER NOT NULL DEFAULT 0;");

        await EnsureColumnAsync(db, "UserAccounts", "DisplayName", "ALTER TABLE \"UserAccounts\" ADD COLUMN \"DisplayName\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Products", "Location", "ALTER TABLE \"Products\" ADD COLUMN \"Location\" TEXT NULL;");
        await EnsureColumnAsync(db, "UserAccounts", "UpdatedAtUtc", "ALTER TABLE \"UserAccounts\" ADD COLUMN \"UpdatedAtUtc\" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000Z'; ",
            "UPDATE \"UserAccounts\" SET \"UpdatedAtUtc\" = \"CreatedAtUtc\" WHERE \"UpdatedAtUtc\" = '0001-01-01T00:00:00.0000000Z';");

        await EnsureColumnAsync(db, "Companies", "ReceiptAddressLine1", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptAddressLine1\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "ReceiptAddressLine2", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptAddressLine2\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "ReceiptPhone", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptPhone\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "ReceiptEmail", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptEmail\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "TaxRegistrationNumber", "ALTER TABLE \"Companies\" ADD COLUMN \"TaxRegistrationNumber\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "ReceiptFooter", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptFooter\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "ReceiptHeaderTitle", "ALTER TABLE \"Companies\" ADD COLUMN \"ReceiptHeaderTitle\" TEXT NOT NULL DEFAULT ''; ");
        await EnsureColumnAsync(db, "Companies", "HeaderImage", "ALTER TABLE \"Companies\" ADD COLUMN \"HeaderImage\" BLOB NULL;");
        await EnsureColumnAsync(db, "Companies", "LogoImage", "ALTER TABLE \"Companies\" ADD COLUMN \"LogoImage\" BLOB NULL;");
        await EnsureColumnAsync(db, "Companies", "LogoScaleMultiplier", "ALTER TABLE \"Companies\" ADD COLUMN \"LogoScaleMultiplier\" INTEGER NOT NULL DEFAULT 1;");
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
