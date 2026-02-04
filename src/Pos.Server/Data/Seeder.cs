using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;

namespace Pos.Server.Data;

public static class Seeder
{
    public static async Task SeedAsync(PosDbContext db)
    {
        await db.Database.MigrateAsync();

        if (await db.Products.AnyAsync())
            return;

        db.Products.AddRange(
            new Product { Sku = "SKU-001", Name = "Bottled Water 500ml", Price = 8.00m },
            new Product { Sku = "SKU-002", Name = "Soft Drink 500ml", Price = 12.00m },
            new Product { Sku = "SKU-003", Name = "Snack Chips", Price = 10.00m },
            new Product { Sku = "SKU-004", Name = "Bread", Price = 18.00m },
            new Product { Sku = "SKU-005", Name = "Milk 1L", Price = 25.00m }
        );

        await db.SaveChangesAsync();
    }
}
