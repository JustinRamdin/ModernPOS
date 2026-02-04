using Microsoft.EntityFrameworkCore;
using Pos.Local.Entities;

namespace Pos.Local.Data;

public class PosLocalDbContext : DbContext
{
    public PosLocalDbContext(DbContextOptions<PosLocalDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<InventoryBalance> Inventory => Set<InventoryBalance>();

    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<SyncState> SyncState => Set<SyncState>();
    public DbSet<DeviceConfig> DeviceConfig => Set<DeviceConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ----- Products -----
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2048);
            e.HasIndex(x => x.Sku).IsUnique();
            e.HasIndex(x => x.UpdatedAtUtc);
        });

        // ----- Customers -----
        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.UpdatedAtUtc);
        });

        // ----- Sales + Lines -----
        modelBuilder.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ReceiptNo).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ReceiptNo).IsUnique();

            e.HasMany(x => x.Lines)
             .WithOne()
             .HasForeignKey(x => x.SaleId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<SaleLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SaleId);
            e.HasIndex(x => x.ProductId);
        });

        // ----- Inventory -----
        modelBuilder.Entity<InventoryBalance>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.LocationCode).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.ProductId, x.LocationCode }).IsUnique();
            e.HasIndex(x => x.UpdatedAtUtc);
        });

        // ----- Outbox -----
        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.SentAtUtc);
            e.HasIndex(x => x.CreatedAtUtc);
        });

        // ----- Key/Value tables -----
        modelBuilder.Entity<SyncState>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
        });

        modelBuilder.Entity<DeviceConfig>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
        });
    }
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();

    public override int SaveChanges()
    {
        TouchUpdatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void TouchUpdatedAt()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAtUtc = utcNow;
        }
    }
}
