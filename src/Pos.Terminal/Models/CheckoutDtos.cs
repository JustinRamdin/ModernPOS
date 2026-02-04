namespace Pos.Terminal.Models;

public record CheckoutRequest(
    string TerminalId,
    List<CheckoutLine> Lines,
    List<CheckoutPayment> Payments
);

public record CheckoutLine(Guid ProductId, decimal Qty);
public record CheckoutPayment(int Method, decimal Amount);

public class CheckoutResponse
{
    public Guid SaleId { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Change { get; set; }
}
