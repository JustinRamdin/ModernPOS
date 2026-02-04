namespace Pos.Local.Entities;

public class InventoryBalance : BaseEntity
{
    public Guid ProductId { get; set; }

    // multi-location
    public string LocationCode { get; set; } = "DEFAULT";

    // for normal items
    public decimal OnHand { get; set; }

    // for length items (total inches)
    public int OnHandInches { get; set; }
}
