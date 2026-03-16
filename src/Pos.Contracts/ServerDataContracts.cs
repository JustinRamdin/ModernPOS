namespace Pos.Contracts;

public sealed record InventoryItemDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
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

public sealed record SalesByDayRowDto(DateOnly Day, int ReceiptCount, decimal GrossTotal);
public sealed record TopProductRowDto(string Name, string? Sku, decimal Qty, decimal Gross);
public sealed record ProfitByProductRowDto(string Name, string? Sku, decimal Qty, decimal Revenue, decimal Cogs, decimal Profit);
public sealed record InventoryValuationRowDto(string Name, string? Sku, decimal OnHand, int OnHandInches, decimal CostPrice, decimal Value);
public sealed record CustomerSalesRowDto(string Name, decimal SalesGross);
