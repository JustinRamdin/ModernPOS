namespace Pos.Application.Tax;

public sealed record VatBreakdown(decimal UnitNet, decimal UnitVat, decimal UnitGross);

public interface IVatCalculator
{
    VatBreakdown Breakdown(decimal enteredSellingPrice, bool vatInclusive);
}

public sealed class VatCalculator : IVatCalculator
{
    public const decimal VatRate = 0.125m;

    private readonly decimal _vatRate;
    private readonly bool _isEnabled;

    public VatCalculator(decimal vatRate = VatRate, bool isEnabled = true)
    {
        if (vatRate < 0) throw new ArgumentOutOfRangeException(nameof(vatRate));

        _vatRate = vatRate;
        _isEnabled = isEnabled;
    }


    private static decimal Money(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public VatBreakdown Breakdown(decimal enteredSellingPrice, bool vatInclusive)
    {
        if (enteredSellingPrice < 0) throw new ArgumentOutOfRangeException(nameof(enteredSellingPrice));
          if (!_isEnabled)
        {
            var price = Money(enteredSellingPrice);
            return new VatBreakdown(price, 0m, price);
        }

        if (vatInclusive)
        {
            // entered is gross
            var gross = Money(enteredSellingPrice);
            var net = Money(gross / (1m + _vatRate));
            var vat = Money(gross - net);
            return new VatBreakdown(net, vat, gross);
        }
        else
        {
            // entered is net
            var net = Money(enteredSellingPrice);
            var vat = Money(net * _vatRate);
            var gross = Money(net + vat);
            return new VatBreakdown(net, vat, gross);
        }
    }
}
