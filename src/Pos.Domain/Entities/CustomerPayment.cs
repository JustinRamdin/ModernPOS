namespace Pos.Domain.Entities;

public class CustomerPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public string? ReferenceNo { get; set; }
    public string? Note { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
}
