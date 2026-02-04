namespace Pos.Domain.Entities;

public enum PaymentMethod { Cash = 1, Card = 2 }

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}
