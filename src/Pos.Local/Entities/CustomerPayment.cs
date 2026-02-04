namespace Pos.Local.Entities;

public sealed class CustomerPayment
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Amount paid toward balance
    public decimal Amount { get; set; }

    // CASH / CREDIT / DEBIT / CHECK
    public string Method { get; set; } = "CASH";

    // optional reference fields
    public string? ReferenceNo { get; set; }
    public string? Note { get; set; }
}
