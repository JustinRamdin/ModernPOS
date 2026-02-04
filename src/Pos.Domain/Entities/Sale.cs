namespace Pos.Domain.Entities;

public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();   // SaleId used for idempotent sync
    public string TerminalId { get; set; } = "";
    public DateTime SoldAtUtc { get; set; } = DateTime.UtcNow;

    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }

    public List<SaleLine> Lines { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}
