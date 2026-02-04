using Pos.Application.Inventory;

namespace Pos.Application.Checkout;

public enum LineQuantityKind
{
    Unit = 0,
    Inches = 1
}

public sealed record CheckoutCartLine(
    Guid ProductId,
    LineQuantityKind QuantityKind,
    decimal Qty,
    int QtyInches);

public sealed record CheckoutLineTotals(
    Guid ProductId,
    string ProductName,
    LineQuantityKind QuantityKind,
    decimal Qty,
    int QtyInches,

    decimal UnitNet,
    decimal UnitVat,
    decimal UnitGross,

    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal);

public sealed record CheckoutTotals(decimal Net, decimal Vat, decimal Gross);
