using System;

namespace Pos.Terminal.Models;

public sealed class ProductDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // entered selling price
    public decimal Price { get; set; }

    // optional grouping in UI (maps to Pos.Local.Entities.Product.Department)
    public string? Department { get; set; }

    public bool VatInclusive { get; set; }
    public bool IsLength { get; set; }
    public string Unit => IsLength ? "in" : "ea";

    // optional stock info (for quick cashier feedback)
    public decimal OnHand { get; set; }
    public int OnHandInches { get; set; }

    public bool IsOutOfStock => IsLength ? OnHandInches <= 0 : OnHand <= 0m;

    public string DisplayPrice => $"${Price:0.00}";

    public string DisplayStock
        => IsLength
            ? $"Stock: {OnHandInches / 12}ft {OnHandInches % 12}in"
            : $"Stock: {OnHand:0.##}";
}
