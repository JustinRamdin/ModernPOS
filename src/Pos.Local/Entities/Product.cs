namespace Pos.Local.Entities;

public class Product : BaseEntity
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public decimal CostPrice { get; set; }

    // this is the "entered selling price" in UI
    public decimal Price { get; set; }

    // VAT & length flags
    public bool VatInclusive { get; set; }
    public bool IsLength { get; set; }

    public bool IsActive { get; set; } = true;

    // Optional metadata
    public string? Department { get; set; }
    public string? Manufacturer { get; set; }
}
