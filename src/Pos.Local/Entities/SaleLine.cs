namespace Pos.Local.Entities;

public enum LineQuantityKind
{
    Unit = 0,
    Inches = 1
}

public class SaleLine : BaseEntity
{
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }

    public LineQuantityKind QuantityKind { get; set; }

    // store both so history stays stable
    public decimal Qty { get; set; }          // for Unit
    public int QtyInches { get; set; }        // for Length

    public decimal UnitNet { get; set; }
    public decimal UnitVat { get; set; }
    public decimal UnitGross { get; set; }

    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
}
