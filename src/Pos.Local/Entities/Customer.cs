using System;
using System.ComponentModel.DataAnnotations;

namespace Pos.Local.Entities;

public sealed class Customer
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string Phone { get; set; } = "";

    [MaxLength(200)]
    public string Email { get; set; } = "";

    // ✅ Needed because your CustomersViewModel uses Customer.Balance
    public decimal Balance { get; set; } = 0m;

    // ✅ Match your existing naming convention used elsewhere
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Optional soft delete (safe to keep even if unused)
    public DateTime? DeletedAtUtc { get; set; }
}
