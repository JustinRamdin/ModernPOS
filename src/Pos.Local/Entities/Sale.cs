namespace Pos.Local.Entities;

public class Sale : BaseEntity
{
    public string ReceiptNo { get; set; } = "";
    public Guid? CustomerId { get; set; }

    // Stable totals (net/vat/gross)
    public decimal NetTotal { get; set; }   // net
    public decimal VatTotal { get; set; }   // vat
    public decimal GrossTotal { get; set; } // gross

    public string Status { get; set; } = "Paid";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SaleLine> Lines { get; set; } = new();
}
