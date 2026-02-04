namespace Pos.Server.Contracts;

public record CheckoutRequest(
    string TerminalId,
    List<CheckoutLine> Lines,
    List<CheckoutPayment> Payments
);

public record CheckoutLine(
    Guid ProductId,
    decimal Qty
);

public record CheckoutPayment(
    int Method,       // 1=cash, 2=card (matches enum)
    decimal Amount
);
