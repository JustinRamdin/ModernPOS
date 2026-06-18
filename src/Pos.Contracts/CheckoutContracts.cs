namespace Pos.Contracts;

public sealed record CheckoutRequest(
    string? TerminalId,
    List<CheckoutLineRequest> Lines,
    List<CheckoutPaymentRequest> Payments,
    Guid? CustomerId = null,
    decimal DiscountAmount = 0m);

public sealed record CheckoutLineRequest(Guid ProductId, decimal Qty);
public sealed record CheckoutPaymentRequest(int Method, decimal Amount);
