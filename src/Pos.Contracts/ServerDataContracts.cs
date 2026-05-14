namespace Pos.Contracts;

public sealed record InventoryItemDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    string? Location,
    decimal CostPrice,
    decimal Price,
    bool VatInclusive,
    bool IsLength,
    decimal OnHand,
    int OnHandInches,
    bool IsActive);

public sealed record UpsertInventoryItemRequest(
    string Sku,
    string Name,
    string? Description,
    string? Location,
    decimal CostPrice,
    decimal Price,
    bool VatInclusive,
    bool IsLength,
    decimal OnHand,
    int OnHandInches,
    bool IsActive = true);

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string Phone,
    string Email,
    string Area,
    decimal Balance,
    bool IsActive);

public sealed record UpsertCustomerRequest(
    string Name,
    string Phone,
    string Email,
    string Area,
    decimal Balance,
    bool IsActive = true);

public sealed record CompanyProfileDto(
    Guid CompanyId,
    string CompanyName,
    string AddressLine1,
    string AddressLine2,
    string Phone,
    string Email,
    string TaxRegistrationNumber,
    string ReceiptFooter,
    string HeaderTitle,
    byte[]? HeaderImage,
    byte[]? LogoImage,
    int LogoScaleMultiplier);

public sealed record UpdateCompanyProfileRequest(
    string CompanyName,
    string AddressLine1,
    string AddressLine2,
    string Phone,
    string Email,
    string TaxRegistrationNumber,
    string ReceiptFooter,
    string HeaderTitle,
    byte[]? HeaderImage,
    byte[]? LogoImage,
    int LogoScaleMultiplier);

public sealed record ReportSummaryDto(
    int ReceiptCount,
    decimal GrossTotal,
    decimal SalesGross,
    decimal Cogs,
    decimal GrossProfit,
    IReadOnlyList<SalesByDayRowDto> SalesByDay,
    IReadOnlyList<TopProductRowDto> TopProducts,
    IReadOnlyList<ProfitByProductRowDto> ProfitByProduct,
    IReadOnlyList<InventoryValuationRowDto> InventoryValuation,
    IReadOnlyList<CustomerSalesRowDto> CustomerSales);

public sealed record SalesByDayRowDto(DateOnly Day, int ReceiptCount, decimal GrossTotal)
{
    public decimal NetTotal => GrossTotal;
    public decimal VatTotal => 0m;
}

public sealed record TopProductRowDto(string Name, string? Sku, decimal Qty, decimal Gross)
{
    public string QuantityDisplay => Qty.ToString("0.##");
    public decimal GrossTotal => Gross;
}

public sealed record ProfitByProductRowDto(string Name, string? Sku, decimal Qty, decimal Revenue, decimal Cogs, decimal Profit)
{
    public string QuantityDisplay => Qty.ToString("0.##");
    public decimal GrossProfit => Profit;
    public decimal GrossMarginPct => Revenue <= 0m ? 0m : Profit / Revenue * 100m;
}

public sealed record InventoryValuationRowDto(string Name, string? Sku, decimal OnHand, int OnHandInches, decimal CostPrice, decimal SellingPrice, decimal CostValue)
{
    public string OnHandDisplay => OnHandInches > 0 ? $"{OnHandInches} in" : OnHand.ToString("0.##");
    public decimal SellingValue => SellingPrice * (OnHandInches > 0 ? OnHandInches : OnHand);
    public decimal Value => CostValue;
    public decimal EstimatedGrossMargin => SellingValue - CostValue;
}

public sealed record InventoryMovementRowDto(DateTime OccurredAtUtc, string Type, string Sku, string Name, string DeltaDisplay, string Reason);

public sealed record LowStockRowDto(string Sku, string Name, string OnHandDisplay, decimal AvgDailyUsageBase, decimal DaysRemaining, decimal SuggestedReorderBase);
public sealed record CustomerSalesRowDto(string Name, int ReceiptCount, decimal SalesGross, decimal CurrentBalance)
{
    public string CustomerName => Name;
    public decimal GrossTotal => SalesGross;
}

public sealed record ServerSalesExportRowDto(
    DateTime OccurredAtUtc,
    string ReceiptNo,
    string Status,
    string? PaymentType,
    string CustomerName,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal);

public sealed record SaleLogLineDto(Guid SaleLineId, Guid ProductId, string ProductName, decimal Qty, decimal UnitPrice, decimal LineTotal);

public sealed record SaleLogEntryDto(Guid SaleId, DateTime SoldAtUtc, string ReceiptNo, decimal Subtotal, decimal Total, string PaymentType, IReadOnlyList<SaleLogLineDto> Lines);

public sealed record SaleItemRefundRequest(Guid SaleLineId, decimal Quantity);