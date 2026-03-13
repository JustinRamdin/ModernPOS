using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;

namespace Pos.Infrastructure.Data;

public class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Price).HasColumnType("numeric(18,2)");
        });

        b.Entity<Sale>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TerminalId).HasMaxLength(50);
            e.HasMany(x => x.Lines).WithOne(x => x.Sale!).HasForeignKey(x => x.SaleId);
            e.HasMany(x => x.Payments).WithOne(x => x.Sale!).HasForeignKey(x => x.SaleId);
            e.Property(x => x.Subtotal).HasColumnType("numeric(18,2)");
            e.Property(x => x.Total).HasColumnType("numeric(18,2)");
        });

        b.Entity<SaleLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Qty).HasColumnType("numeric(18,3)");
            e.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)");
            e.Property(x => x.LineTotal).HasColumnType("numeric(18,2)");
        });

        b.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        });

        b.Entity<OutboxEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100);
        });
        
        b.Entity<Company>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<UserAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(120);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.HasIndex(x => new { x.CompanyId, x.Username }).IsUnique();
            e.HasOne(x => x.Company).WithMany(x => x.Users).HasForeignKey(x => x.CompanyId);
        });
    }
}
