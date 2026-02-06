using System;

namespace Pos.Local.Services;

// --- SALES ---
public sealed record SalesSummaryDto(
    int ReceiptCount,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    decimal AverageGross
);

public sealed record SalesByDayRowDto(
    DateOnly Day,
    int ReceiptCount,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal
);

public sealed record TopProductRowDto(
    Guid ProductId,
    string Sku,
    string Name,
    string QuantityDisplay,
    decimal GrossTotal
);

// --- PROFIT ---
public sealed record ProfitSummaryDto(
    decimal SalesGross,
    decimal Cogs,
    decimal GrossProfit,
    decimal GrossMarginPct
);

public sealed record ProfitByProductRowDto(
    Guid ProductId,
    string Sku,
    string Name,
    string QuantityDisplay,
    decimal SalesGross,
    decimal Cogs,
    decimal GrossProfit,
    decimal GrossMarginPct
);

// --- VAT ---
public sealed record VatSummaryDto(
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal
);

// --- TENDERS ---
public sealed record TenderSummaryDto(
    decimal CashTotal,
    decimal DebitTotal,
    decimal CreditTotal,
    decimal OnAccountTotal,
    decimal ChangeGivenTotal,
    decimal ExpectedCashInDrawer
);

// --- INVENTORY ---
public sealed record InventoryValuationRowDto(
    Guid ProductId,
    string Sku,
    string Name,
    string OnHandDisplay,
    decimal SellingValue,
    decimal CostValue,
    decimal EstimatedGrossMargin
);

public sealed record LowStockRowDto(
    Guid ProductId,
    string Sku,
    string Name,
    string OnHandDisplay,
    decimal AvgDailyUsageBase,
    decimal DaysRemaining,
    decimal SuggestedReorderBase
);

public sealed record InventoryMovementRowDto(
    DateTime OccurredAtUtc,
    string Type,     // "SALE" | "ADJUST"
    string Sku,
    string Name,
    string DeltaDisplay,
    string Reason
);

// --- CUSTOMERS ---
public sealed record CustomerSalesRowDto(
    Guid CustomerId,
    string CustomerName,
    int ReceiptCount,
    decimal GrossTotal,
    decimal CurrentBalance
);

// --- EXPORTS ---
public sealed record SalesExportRowDto
{
    public SalesExportRowDto(
        DateTime occurredAtUtc,
        string receiptNo,
        string status,
        string? paymentType,
        string customerName,
        decimal netTotal,
        decimal vatTotal,
        decimal grossTotal)
    {
        OccurredAtUtc = occurredAtUtc;
        ReceiptNo = receiptNo;
        Status = status;
        PaymentType = paymentType;
        CustomerName = customerName;
        NetTotal = netTotal;
        VatTotal = vatTotal;
        GrossTotal = grossTotal;
    }

    public DateTime OccurredAtUtc { get; init; }
    public string ReceiptNo { get; init; }
    public string Status { get; init; }
    public string? PaymentType { get; init; }
    public string CustomerName { get; init; }
    public decimal NetTotal { get; init; }
    public decimal VatTotal { get; init; }
    public decimal GrossTotal { get; init; }
}

public sealed record PurchaseExportRowDto(
    DateTime OccurredAtUtc,
    string Sku,
    string Name,
    string QuantityDisplay,
    string Reason
);
