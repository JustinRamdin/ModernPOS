namespace Pos.Domain.Entities;

public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid? RefundedFromSaleLineId { get; set; }

    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatTotal { get; set; }
    public decimal LineTotal { get; set; }
}
