using Pos.Application.Tax;

namespace Pos.Application.Checkout;

public sealed class CheckoutCalculator
{
    private readonly IVatCalculator _vat;

    public CheckoutCalculator(IVatCalculator vat)
    {
        _vat = vat;
    }

    private static decimal Money(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public CheckoutLineTotals CalculateLine(
        Guid productId,
        string productName,
        decimal enteredSellingPrice,
        bool vatInclusive,
        LineQuantityKind quantityKind,
        decimal qty,
        int qtyInches)
    {
        var b = _vat.Breakdown(enteredSellingPrice, vatInclusive);

        decimal multiplier = quantityKind == LineQuantityKind.Unit
            ? qty
            : qtyInches;

        var netTotal = Money(b.UnitNet * multiplier);
        var vatTotal = Money(b.UnitVat * multiplier);
        var grossTotal = Money(b.UnitGross * multiplier);

        return new CheckoutLineTotals(
            productId,
            productName,
            quantityKind,
            qty,
            qtyInches,
            b.UnitNet,
            b.UnitVat,
            b.UnitGross,
            netTotal,
            vatTotal,
            grossTotal
        );
    }

    public CheckoutTotals SumTotals(IEnumerable<CheckoutLineTotals> lines)
    {
        var net = Money(lines.Sum(x => x.NetTotal));
        var vat = Money(lines.Sum(x => x.VatTotal));
        var gross = Money(lines.Sum(x => x.GrossTotal));
        return new CheckoutTotals(net, vat, gross);
    }
}
