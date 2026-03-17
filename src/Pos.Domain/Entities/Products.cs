namespace Pos.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Location { get; set; }
    public decimal CostPrice { get; set; }
    public decimal Price { get; set; }
    public bool VatInclusive { get; set; }
    public bool IsLength { get; set; }
    public decimal OnHand { get; set; }
    public int OnHandInches { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
