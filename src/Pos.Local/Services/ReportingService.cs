using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Measurements;
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
        var saleIds = await _db.Sales.AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync();

        if (saleIds.Count == 0)
            return new List<string>();

        var paymentMethods = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "sale" && saleIds.Contains(o.EntityId))
            .Select(o => o.PayloadJson)
            .ToListAsync();

        var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in paymentMethods)
        {
            if (TryGetPaymentInfo(payload, out var info) && !string.IsNullOrWhiteSpace(info.Method))
                methods.Add(info.Method);
        }

        return methods.OrderBy(x => x).ToList();
    }

    public async Task<List<string>> GetCustomerNamesAsync()
    {
        return await _db.Customers.AsNoTracking()
            .Where(c => c.DeletedAtUtc == null)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync();
    }

    public Task<List<string>> GetItemOrSkuListAsync(string locationCode, CancellationToken ct = default)
    {
         return _db.Products.AsNoTracking()
            .Where(p => p.DeletedAtUtc == null && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => string.IsNullOrWhiteSpace(p.Sku) ? p.Name : $"{p.Sku} - {p.Name}")
            .ToListAsync(ct);
    }

    // ========= SALES EXPORT (FILTERED) =========
    // This matches what the ViewModel expects.

    public async Task<List<SalesExportRowDto>> GetSalesExportAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string? paymentType = null,
        string? customer = null,
        string? itemOrSku = null,
        string? search = null,
        CancellationToken ct = default)
    {
        
        var salesQuery = _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc);

        if (!string.IsNullOrWhiteSpace(customer))
        {
            salesQuery = salesQuery.Where(s => s.CustomerId != null);
        }

        var sales = await salesQuery
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.CreatedAtUtc,
                s.ReceiptNo,
                s.Status,
                s.NetTotal,
                s.VatTotal,
                s.GrossTotal,
                s.CustomerId
            })
            .ToListAsync(ct);

        var customerIds = sales
            .Where(s => s.CustomerId != null)
            .Select(s => s.CustomerId!.Value)
            .Distinct()
            .ToList();

        var customerNames = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var paymentInfo = await GetPaymentInfoAsync(sales.Select(s => s.Id).ToList(), ct);

        var rows = sales.Select(s =>
        {
            paymentInfo.TryGetValue(s.Id, out var info);
            var customerName = s.CustomerId != null && customerNames.TryGetValue(s.CustomerId.Value, out var name)
                ? name
                : "Walk-in";

            return new
            {
                s.Id,
                Row = new SalesExportRowDto(
                    s.CreatedAtUtc,
                    s.ReceiptNo,
                    s.Status,
                    info?.Method,
                    customerName,
                    s.NetTotal,
                    s.VatTotal,
                    s.GrossTotal
                )
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(paymentType))
        {
            rows = rows
                .Where(r => string.Equals(r.Row.PaymentType, paymentType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(customer))
        {
            rows = rows
                .Where(r => r.Row.CustomerName.Contains(customer, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows
                .Where(r => r.Row.ReceiptNo.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(itemOrSku))
        {
            var productIds = await _db.Products.AsNoTracking()
                .Where(p => p.DeletedAtUtc == null && p.IsActive)
                .Where(p => (p.Sku + " - " + p.Name).Contains(itemOrSku, StringComparison.OrdinalIgnoreCase)
                         || p.Sku.Contains(itemOrSku, StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains(itemOrSku, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToListAsync(ct);

            if (productIds.Count > 0)
            {
                var saleIds = await _db.SaleLines.AsNoTracking()
                    .Where(l => productIds.Contains(l.ProductId))
                    .Select(l => l.SaleId)
                    .Distinct()
                    .ToListAsync(ct);

                rows = rows
                    .Where(r => saleIds.Contains(r.Id))
                    .ToList();
            }
        }

        return rows.Select(r => r.Row).ToList();
    }

    // ========= EXISTING METHODS YOU ALREADY HAVE =========
    // Keep your current implementations for these; below are placeholders to show signatures.

    public Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime fromUtc, DateTime toUtc)
        {
        return GetSalesSummaryInternalAsync(fromUtc, toUtc);
    }

    public Task<List<SalesByDayRowDto>> GetSalesByDayAsync(DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz)
        => GetSalesByDayInternalAsync(fromUtc, toUtc, tz);

    public Task<ProfitSummaryDto> GetProfitSummaryAsync(DateTime fromUtc, DateTime toUtc)
        => GetProfitSummaryInternalAsync(fromUtc, toUtc);

    public Task<TenderSummaryDto> GetTenderSummaryAsync(DateTime fromUtc, DateTime toUtc)
        => GetTenderSummaryInternalAsync(fromUtc, toUtc);

    public Task<List<InventoryMovementRowDto>> GetInventoryMovementsAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
        => GetInventoryMovementsInternalAsync(fromUtc, toUtc, locationCode);

    public Task<List<PurchaseExportRowDto>> GetPurchaseAdjustmentsAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
        => GetPurchaseAdjustmentsInternalAsync(fromUtc, toUtc, locationCode);

    public Task<List<CustomerSalesRowDto>> GetCustomerSalesAsync(DateTime fromUtc, DateTime toUtc)
        => GetCustomerSalesInternalAsync(fromUtc, toUtc);

    public Task<List<InventoryValuationRowDto>> GetInventoryValuationAsync(string locationCode)
        => GetInventoryValuationInternalAsync(locationCode);

     public Task<List<LowStockRowDto>> GetLowStockAsync(string locationCode, int rangeDays, decimal suggestedReorderDays)
        => GetLowStockInternalAsync(locationCode, rangeDays, suggestedReorderDays);

        public Task<List<TopProductRowDto>> GetTopProductsAsync(DateTime fromUtc, DateTime toUtc, int topN)
                => GetTopProductsInternalAsync(fromUtc, toUtc, topN);

        public Task<List<ProfitByProductRowDto>> GetProfitByProductAsync(DateTime fromUtc, DateTime toUtc, int maxRows)
                => GetProfitByProductInternalAsync(fromUtc, toUtc, maxRows);
        private async Task<SalesSummaryDto> GetSalesSummaryInternalAsync(DateTime fromUtc, DateTime toUtc)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .ToListAsync();

        var count = sales.Count;
        var net = RoundMoney(sales.Sum(s => s.NetTotal));
        var vat = RoundMoney(sales.Sum(s => s.VatTotal));
        var gross = RoundMoney(sales.Sum(s => s.GrossTotal));
        var avg = count == 0 ? 0m : RoundMoney(gross / count);

        return new SalesSummaryDto(count, net, vat, gross, avg);
    }

    private async Task<List<SalesByDayRowDto>> GetSalesByDayInternalAsync(DateTime fromUtc, DateTime toUtc, TimeZoneInfo tz)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .ToListAsync();

        return sales
            .GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s.CreatedAtUtc, tz).Date))
            .OrderBy(g => g.Key)
            .Select(g => new SalesByDayRowDto(
                g.Key,
                g.Count(),
                RoundMoney(g.Sum(x => x.NetTotal)),
                RoundMoney(g.Sum(x => x.VatTotal)),
                RoundMoney(g.Sum(x => x.GrossTotal))
            ))
            .ToList();
    }

    private async Task<List<TopProductRowDto>> GetTopProductsInternalAsync(DateTime fromUtc, DateTime toUtc, int topN)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc),
                line => line.SaleId,
                sale => sale.Id,
                (line, sale) => line)
            .ToListAsync();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        return lines
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                products.TryGetValue(g.Key, out var product);
                var isLength = product?.IsLength ?? false;
                var qty = g.Sum(x => x.Qty);
                var qtyInches = g.Sum(x => x.QtyInches);
                var gross = RoundMoney(g.Sum(x => x.GrossTotal));

                return new TopProductRowDto(
                    g.Key,
                    product?.Sku ?? "",
                    product?.Name ?? "Unknown",
                    FormatQuantityDisplay(isLength, qty, qtyInches),
                    gross
                );
            })
            .OrderByDescending(x => x.GrossTotal)
            .Take(topN)
            .ToList();
    }

    private async Task<ProfitSummaryDto> GetProfitSummaryInternalAsync(DateTime fromUtc, DateTime toUtc)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc),
                line => line.SaleId,
                sale => sale.Id,
                (line, sale) => line)
            .ToListAsync();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var salesGross = RoundMoney(lines.Sum(l => l.GrossTotal));
        var cogs = RoundMoney(lines.Sum(l =>
        {
            if (!products.TryGetValue(l.ProductId, out var product))
                return 0m;

            var qty = product.IsLength ? l.QtyInches : l.Qty;
            return product.CostPrice * qty;
        }));

        var grossProfit = RoundMoney(salesGross - cogs);
        var marginPct = salesGross == 0m ? 0m : Math.Round(grossProfit / salesGross * 100m, 2, MidpointRounding.AwayFromZero);

        return new ProfitSummaryDto(salesGross, cogs, grossProfit, marginPct);
    }

    private async Task<List<ProfitByProductRowDto>> GetProfitByProductInternalAsync(DateTime fromUtc, DateTime toUtc, int maxRows)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc),
                line => line.SaleId,
                sale => sale.Id,
                (line, sale) => line)
            .ToListAsync();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        return lines
            .GroupBy(l => l.ProductId)
            .Select(g =>
            {
                products.TryGetValue(g.Key, out var product);
                var isLength = product?.IsLength ?? false;
                var qty = g.Sum(x => x.Qty);
                var qtyInches = g.Sum(x => x.QtyInches);
                var salesGross = RoundMoney(g.Sum(x => x.GrossTotal));
                var cogs = RoundMoney(g.Sum(x =>
                {
                    if (product == null) return 0m;
                    var useQty = product.IsLength ? x.QtyInches : x.Qty;
                    return product.CostPrice * useQty;
                }));
                var grossProfit = RoundMoney(salesGross - cogs);
                var marginPct = salesGross == 0m ? 0m : Math.Round(grossProfit / salesGross * 100m, 2, MidpointRounding.AwayFromZero);

                return new ProfitByProductRowDto(
                    g.Key,
                    product?.Sku ?? "",
                    product?.Name ?? "Unknown",
                    FormatQuantityDisplay(isLength, qty, qtyInches),
                    salesGross,
                    cogs,
                    grossProfit,
                    marginPct
                );
            })
            .OrderByDescending(x => x.GrossProfit)
            .Take(maxRows)
            .ToList();
    }

    private async Task<TenderSummaryDto> GetTenderSummaryInternalAsync(DateTime fromUtc, DateTime toUtc)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .Select(s => new { s.Id, s.GrossTotal, s.Status })
            .ToListAsync();

        if (sales.Count == 0)
            return new TenderSummaryDto(0m, 0m, 0m, 0m, 0m, 0m);

        var paymentInfo = await GetPaymentInfoAsync(sales.Select(s => s.Id).ToList(), CancellationToken.None);

        decimal cashTotal = 0m;
        decimal debitTotal = 0m;
        decimal creditTotal = 0m;
        decimal onAccountTotal = 0m;
        decimal changeGiven = 0m;
        decimal expectedCash = 0m;

        foreach (var sale in sales)
        {
            paymentInfo.TryGetValue(sale.Id, out var info);
            var method = info?.Method ?? sale.Status;

            switch (method?.ToUpperInvariant())
            {
                case "CASH":
                    cashTotal += sale.GrossTotal;
                    changeGiven += info?.Change ?? 0m;
                    if (info != null && info.CashGiven > 0m)
                        expectedCash += info.CashGiven - info.Change;
                    else
                        expectedCash += sale.GrossTotal;
                    break;
                case "DEBIT":
                    debitTotal += sale.GrossTotal;
                    break;
                case "CREDIT":
                    creditTotal += sale.GrossTotal;
                    break;
                case "ON_ACCOUNT":
                case "ONACCOUNT":
                    onAccountTotal += sale.GrossTotal;
                    break;
            }
        }

        return new TenderSummaryDto(
            RoundMoney(cashTotal),
            RoundMoney(debitTotal),
            RoundMoney(creditTotal),
            RoundMoney(onAccountTotal),
            RoundMoney(changeGiven),
            RoundMoney(expectedCash)
        );
    }

    private async Task<List<InventoryMovementRowDto>> GetInventoryMovementsInternalAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
    {
        var movementRows = new List<InventoryMovementRowDto>();

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .Select(s => new { s.Id, s.CreatedAtUtc, s.ReceiptNo })
            .ToListAsync();

        var saleIds = sales.Select(s => s.Id).ToList();
        var saleLines = await _db.SaleLines.AsNoTracking()
            .Where(l => saleIds.Contains(l.SaleId))
            .ToListAsync();

        var productIds = saleLines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var saleMap = sales.ToDictionary(s => s.Id);

        foreach (var line in saleLines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                continue;

            var sale = saleMap[line.SaleId];
            var qty = line.Qty * -1m;
            var qtyInches = line.QtyInches * -1;

            movementRows.Add(new InventoryMovementRowDto(
                sale.CreatedAtUtc,
                "SALE",
                product.Sku,
                product.Name,
                FormatDeltaDisplay(product.IsLength, qty, qtyInches),
                $"Receipt {sale.ReceiptNo}"
            ));
        }

        var adjustmentPayloads = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "inventory_adjustment" && o.CreatedAtUtc >= fromUtc && o.CreatedAtUtc < toUtc)
            .Select(o => o.PayloadJson)
            .ToListAsync();

        foreach (var payload in adjustmentPayloads)
        {
            if (!TryParseInventoryAdjustment(payload, out var adjustment))
                continue;

            if (!string.Equals(adjustment.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!products.TryGetValue(adjustment.ProductId, out var product))
            {
                product = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == adjustment.ProductId);
                if (product != null)
                    products[product.Id] = product;
            }

            if (product == null)
                continue;

            var qtyDelta = product.IsLength ? (int)Math.Round(adjustment.Delta) : 0;
            var unitDelta = product.IsLength ? 0m : adjustment.Delta;

            movementRows.Add(new InventoryMovementRowDto(
                adjustment.OccurredAtUtc,
                "ADJUST",
                product.Sku,
                product.Name,
                FormatDeltaDisplay(product.IsLength, unitDelta, qtyDelta),
                adjustment.Reason
            ));
        }

        return movementRows
            .OrderByDescending(r => r.OccurredAtUtc)
            .ToList();
    }

    private async Task<List<PurchaseExportRowDto>> GetPurchaseAdjustmentsInternalAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
    {
        var adjustments = new List<PurchaseExportRowDto>();

        var payloads = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "inventory_adjustment" && o.CreatedAtUtc >= fromUtc && o.CreatedAtUtc < toUtc)
            .Select(o => o.PayloadJson)
            .ToListAsync();

        foreach (var payload in payloads)
        {
            if (!TryParseInventoryAdjustment(payload, out var adjustment))
                continue;

            if (!string.Equals(adjustment.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase))
                continue;

            if (adjustment.Delta <= 0m)
                continue;

            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == adjustment.ProductId);

            if (product == null)
                continue;

            var qtyDisplay = product.IsLength
                ? FormatQuantityDisplay(true, 0m, (int)Math.Round(adjustment.Delta))
                : FormatQuantityDisplay(false, adjustment.Delta, 0);

            adjustments.Add(new PurchaseExportRowDto(
                adjustment.OccurredAtUtc,
                product.Sku,
                product.Name,
                qtyDisplay,
                adjustment.Reason
            ));
        }

        return adjustments
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToList();
    }

    private async Task<List<CustomerSalesRowDto>> GetCustomerSalesInternalAsync(DateTime fromUtc, DateTime toUtc)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .Where(s => s.CustomerId != null)
            .Select(s => new { s.CustomerId, s.GrossTotal })
            .ToListAsync();

        var customerIds = sales
            .Where(s => s.CustomerId != null)
            .Select(s => s.CustomerId!.Value)
            .Distinct()
            .ToList();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        return sales
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g =>
            {
                customers.TryGetValue(g.Key, out var customer);
                return new CustomerSalesRowDto(
                    g.Key,
                    customer?.Name ?? "Unknown",
                    g.Count(),
                    RoundMoney(g.Sum(x => x.GrossTotal)),
                    customer?.Balance ?? 0m
                );
            })
            .OrderByDescending(r => r.GrossTotal)
            .ToList();
    }

    private async Task<List<InventoryValuationRowDto>> GetInventoryValuationInternalAsync(string locationCode)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.DeletedAtUtc == null && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var inventory = await _db.Inventory.AsNoTracking()
            .Where(i => i.LocationCode == locationCode)
            .ToDictionaryAsync(i => i.ProductId);

        return products.Select(p =>
        {
            inventory.TryGetValue(p.Id, out var bal);
            var onHand = bal?.OnHand ?? 0m;
            var onHandInches = bal?.OnHandInches ?? 0;
            var multiplier = p.IsLength ? onHandInches : onHand;
            var selling = RoundMoney(p.Price * multiplier);
            var cost = RoundMoney(p.CostPrice * multiplier);
            var margin = RoundMoney(selling - cost);

            return new InventoryValuationRowDto(
                p.Id,
                p.Sku,
                p.Name,
                FormatQuantityDisplay(p.IsLength, onHand, onHandInches),
                selling,
                cost,
                margin
            );
        }).ToList();
    }

    private async Task<List<LowStockRowDto>> GetLowStockInternalAsync(string locationCode, int rangeDays, decimal suggestedReorderDays)
    {
        var rangeStart = DateTime.UtcNow.AddDays(-rangeDays);
        var rangeEnd = DateTime.UtcNow;

        var salesLines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= rangeStart && s.CreatedAtUtc < rangeEnd),
                line => line.SaleId,
                sale => sale.Id,
                (line, sale) => line)
            .ToListAsync();

        var productIds = salesLines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.DeletedAtUtc == null && p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        var inventory = await _db.Inventory.AsNoTracking()
            .Where(i => i.LocationCode == locationCode)
            .ToDictionaryAsync(i => i.ProductId);

        var lowStock = new List<LowStockRowDto>();

        foreach (var group in salesLines.GroupBy(l => l.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product))
                continue;

            inventory.TryGetValue(group.Key, out var bal);
            var onHand = bal?.OnHand ?? 0m;
            var onHandInches = bal?.OnHandInches ?? 0;
            var totalSold = product.IsLength
                ? group.Sum(x => x.QtyInches)
                : group.Sum(x => x.Qty);

            var avgDaily = rangeDays <= 0 ? 0m : totalSold / rangeDays;
            if (avgDaily <= 0m)
                continue;

            var onHandBase = product.IsLength ? onHandInches : onHand;
            var daysRemaining = avgDaily == 0m ? 0m : onHandBase / avgDaily;
            var suggestedReorder = avgDaily * suggestedReorderDays;

            if (daysRemaining > suggestedReorderDays)
                continue;

            lowStock.Add(new LowStockRowDto(
                product.Id,
                product.Sku,
                product.Name,
                FormatQuantityDisplay(product.IsLength, onHand, onHandInches),
                RoundMoney(avgDaily),
                RoundMoney(daysRemaining),
                RoundMoney(suggestedReorder)
            ));
        }

        return lowStock
            .OrderBy(r => r.DaysRemaining)
            .ToList();
    }

    private static string FormatQuantityDisplay(bool isLength, decimal qty, int qtyInches)
    {
        if (!isLength)
            return qty.ToString("0.###");

        if (qtyInches < 0)
            qtyInches = 0;

        var fi = LengthConverter.FromTotalInches(qtyInches);
        return $"{fi.Feet} ft {fi.Inches} in ({qtyInches} in)";
    }

    private static string FormatDeltaDisplay(bool isLength, decimal qty, int qtyInches)
    {
        if (!isLength)
            return qty.ToString("+0.###;-0.###;0");

        var sign = qtyInches < 0 ? "-" : "+";
        var absInches = Math.Abs(qtyInches);
        var fi = LengthConverter.FromTotalInches(absInches);
        return $"{sign}{fi.Feet} ft {fi.Inches} in ({absInches} in)";
    }

    private static decimal RoundMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private async Task<Dictionary<Guid, PaymentInfo>> GetPaymentInfoAsync(List<Guid> saleIds, CancellationToken ct)
    {
        if (saleIds.Count == 0)
            return new Dictionary<Guid, PaymentInfo>();

        var payloads = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "sale" && saleIds.Contains(o.EntityId))
            .Select(o => new { o.EntityId, o.PayloadJson })
            .ToListAsync(ct);

        var info = new Dictionary<Guid, PaymentInfo>();
        foreach (var payload in payloads)
        {
            if (TryGetPaymentInfo(payload.PayloadJson, out var payment))
                info[payload.EntityId] = payment;
        }

        return info;
    }

    private static bool TryGetPaymentInfo(string payloadJson, out PaymentInfo info)
    {
        info = new PaymentInfo("UNKNOWN", 0m, 0m);
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject root || root["payment"] is not JsonObject payment)
            return false;

        var method = payment["method"]?.GetValue<string>() ?? "UNKNOWN";
        var cashGiven = payment["cash_given"]?.GetValue<decimal?>() ?? 0m;
        var change = payment["change"]?.GetValue<decimal?>() ?? 0m;

        info = new PaymentInfo(method, cashGiven, change);
        return true;
    }

    private static bool TryParseInventoryAdjustment(string payloadJson, out InventoryAdjustmentInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject root)
            return false;

        if (!Guid.TryParse(root["product_id"]?.GetValue<string>(), out var productId))
            return false;

        var delta = root["delta"]?.GetValue<decimal?>() ?? 0m;
        var reason = root["reason"]?.GetValue<string>() ?? "Adjustment";
        var location = root["location_code"]?.GetValue<string>() ?? "DEFAULT";
        var occurredText = root["occurred_at_utc"]?.GetValue<string>();
        var occurredAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(occurredText) && DateTime.TryParse(occurredText, out var parsed))
            occurredAt = parsed;

        info = new InventoryAdjustmentInfo(productId, delta, reason, location, occurredAt);
        return true;
    }

    private sealed record PaymentInfo(string Method, decimal CashGiven, decimal Change);

    private readonly record struct InventoryAdjustmentInfo(
        Guid ProductId,
        decimal Delta,
        string Reason,
        string LocationCode,
        DateTime OccurredAtUtc);

}
