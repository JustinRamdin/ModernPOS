namespace Pos.Application.Inventory;

public sealed record StockAdjustResult(bool Success, string? ErrorMessage);

public enum StockKind
{
    Unit = 0,
    Length = 1
}

public static class StockService
{
    public static StockAdjustResult TrySubtractUnits(
        decimal onHand,
        decimal qtyToSubtract,
        bool allowNegative,
        out decimal newOnHand)
    {
        newOnHand = onHand;

        if (qtyToSubtract <= 0)
            return new StockAdjustResult(false, "Quantity must be greater than zero.");

        var candidate = onHand - qtyToSubtract;

        if (!allowNegative && candidate < 0)
            return new StockAdjustResult(false, "Insufficient stock.");

        newOnHand = candidate;
        return new StockAdjustResult(true, null);
    }

    public static StockAdjustResult TrySubtractInches(
        int onHandInches,
        int inchesToSubtract,
        bool allowNegative,
        out int newOnHandInches)
    {
        newOnHandInches = onHandInches;

        if (inchesToSubtract <= 0)
            return new StockAdjustResult(false, "Length must be greater than zero.");

        var candidate = onHandInches - inchesToSubtract;

        if (!allowNegative && candidate < 0)
            return new StockAdjustResult(false, "Insufficient stock.");

        newOnHandInches = candidate;
        return new StockAdjustResult(true, null);
    }
}
