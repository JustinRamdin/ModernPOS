namespace Pos.Terminal.Models;

public enum PaymentMethod
{
    None = 0,
    Cash = 1,
    Debit = 2,
    Credit = 3,
    OnAccount = 4
}

public sealed record PaymentResult(PaymentMethod Method, decimal CashTendered);
