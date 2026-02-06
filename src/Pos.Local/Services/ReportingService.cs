using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;

namespace Pos.Local.Services;

public sealed class ReportingService
{
    private readonly PosLocalDbContext _db;

    public ReportingService(PosLocalDbContext db)
    {
        _db = db;
    }

    // ========= FILTER OPTION LISTS =========

    public async Task<List<string>> GetPaymentTypesAsync()
    {
        // TODO: Replace with your real table/field.
        // Example idea:
        // return await _db.Sales.AsNoTracking()
        //    .Select(s => s.PaymentType)
        //    .Where(x => x != null && x != "")
        //    .Distinct()
        //    .OrderBy(x => x)
        //    .ToListAsync();

        return new List<string>(); // safe default
    }

    public async Task<List<string>> GetCustomerNamesAsync()
    {
        // TODO: Replace with your real table/field.
        return new List<string>();
    }

    public async Task<List<string>> GetItemOrSkuListAsync(string locationCode)
    {
        // TODO: Replace with your real inventory/sales lines table/field.
        return new List<string>();
    }

    // ========= SALES EXPORT (FILTERED) =========
    // This matches what the ViewModel expects.

    public async Task<List<SalesExportRow>> GetSalesExportAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string? paymentType = null,
        string? customer = null,
        string? itemOrSku = null,
        string? search = null,
        CancellationToken ct = default)
    {
        // TODO: Replace with your real query. This is a placeholder.

        // Example logic (pseudo):
        // var q = _db.Sales.AsNoTracking()
        //   .Where(s => s.OccurredAtUtc >= fromUtc && s.OccurredAtUtc < toUtc);
        //
        // if (paymentType != null) q = q.Where(s => s.PaymentType == paymentType);
        // if (customer != null) q = q.Where(s => s.CustomerName == customer);
        // if (search != null) q = q.Where(s => s.ReceiptNo.Contains(search) || s.CustomerName.Contains(search));
        // if (itemOrSku != null) q = q.Where(s => s.Lines.Any(l => l.Sku == itemOrSku || l.ItemName == itemOrSku));
        //
        // return await q.Select(s => new SalesExportRow { ... }).ToListAsync(ct);

        await Task.CompletedTask;
        return new List<SalesExportRow>();
    }

    // ========= EXISTING METHODS YOU ALREADY HAVE =========
    // Keep your current implementations for these; below are placeholders to show signatures.

    public Task<List<PurchaseAdjustmentRow>> GetPurchaseAdjustmentsAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
        => Task.FromResult(new List<PurchaseAdjustmentRow>());

    public Task<List<CustomerSalesRow>> GetCustomerSalesAsync(DateTime fromUtc, DateTime toUtc)
        => Task.FromResult(new List<CustomerSalesRow>());

    public Task<List<InventoryValuationRow>> GetInventoryValuationAsync(string locationCode)
        => Task.FromResult(new List<InventoryValuationRow>());

    public Task<List<LowStockRow>> GetLowStockAsync(string locationCode, int rangeDays, decimal suggestedReorderDays)
        => Task.FromResult(new List<LowStockRow>());

    public Task<List<TopProductRow>> GetTopProductsAsync(DateTime fromUtc, DateTime toUtc, int topN)
        => Task.FromResult(new List<TopProductRow>());

    public Task<List<ProfitByProductRow>> GetProfitByProductAsync(DateTime fromUtc, DateTime toUtc, int maxRows)
        => Task.FromResult(new List<ProfitByProductRow>());
}


// ========= DTOs expected by ViewModel =========
// If you already have these in your current ReportingService.cs, remove duplicates and use yours instead.

public sealed class SalesExportRow
{
    public DateTime OccurredAtUtc { get; set; }
    public string ReceiptNo { get; set; } = "";
    public string Status { get; set; } = "";
    public string? PaymentType { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
}

public sealed class PurchaseAdjustmentRow
{
    public DateTime OccurredAtUtc { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string QuantityDisplay { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CustomerSalesRow
{
    public string CustomerName { get; set; } = "";
    public int ReceiptCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal CurrentBalance { get; set; }
}

public sealed class InventoryValuationRow
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string OnHandDisplay { get; set; } = "";
    public decimal SellingValue { get; set; }
    public decimal CostValue { get; set; }
    public decimal EstimatedGrossMargin { get; set; }
}

public sealed class LowStockRow
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string OnHandDisplay { get; set; } = "";
    public decimal AvgDailyUsageBase { get; set; }
    public decimal DaysRemaining { get; set; }
    public decimal SuggestedReorderBase { get; set; }
}

public sealed class TopProductRow
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string QuantityDisplay { get; set; } = "";
    public decimal GrossTotal { get; set; }
}

public sealed class ProfitByProductRow
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string QuantityDisplay { get; set; } = "";
    public decimal SalesGross { get; set; }
    public decimal Cogs { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossMarginPct { get; set; }
}
