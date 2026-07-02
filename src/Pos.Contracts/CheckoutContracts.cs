namespace Pos.Contracts;

public sealed record CheckoutRequest(
    string? TerminalId,
    List<CheckoutLineRequest> Lines,
    List<CheckoutPaymentRequest> Payments,
    Guid? CustomerId = null,
    decimal DiscountAmount = 0m,
    decimal? NetSubtotal = null,
    decimal? VatTotal = null,
    decimal? TotalDue = null);

public sealed record CheckoutLineRequest(
    Guid ProductId,
    decimal Qty,
    decimal? OverrideUnitPrice = null,
    decimal? VatTotal = null,
    decimal? GrossTotal = null);
public sealed record CheckoutPaymentRequest(int Method, decimal Amount);

public static class CheckoutSpecialProducts
{
    public static readonly Guid MiscellaneousId = Guid.Parse("4d495343-454c-4c41-4e45-4f5553495445");
    public const string MiscellaneousSku = "__MISC__";
}
