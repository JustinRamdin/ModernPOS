namespace Pos.Application.Tax;

public sealed record VatBreakdown(decimal UnitNet, decimal UnitVat, decimal UnitGross);

public interface IVatCalculator
{
    VatBreakdown Breakdown(decimal enteredSellingPrice, bool vatInclusive);
}

public sealed class VatCalculator : IVatCalculator
{
    public const decimal VatRate = 0.125m;

    private static decimal Money(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public VatBreakdown Breakdown(decimal enteredSellingPrice, bool vatInclusive)
    {
        if (enteredSellingPrice < 0) throw new ArgumentOutOfRangeException(nameof(enteredSellingPrice));

        if (vatInclusive)
        {
            // entered is gross
            var gross = Money(enteredSellingPrice);
            var net = Money(gross / (1m + VatRate));
            var vat = Money(gross - net);
            return new VatBreakdown(net, vat, gross);
        }
        else
        {
            // entered is net
            var net = Money(enteredSellingPrice);
            var vat = Money(net * VatRate);
            var gross = Money(net + vat);
            return new VatBreakdown(net, vat, gross);
        }
    }
}
