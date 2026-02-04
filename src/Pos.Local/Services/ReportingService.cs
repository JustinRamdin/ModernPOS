using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using Pos.Local.Entities;

namespace Pos.Local.Services;

public interface IReportingService
{
    Task<IReadOnlyList<SalesExportRowDto>> GetSalesExportAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);
    Task<IReadOnlyList<PurchaseExportRowDto>> GetPurchaseAdjustmentsAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        string locationCode = "DEFAULT");
    Task<IReadOnlyList<CustomerSalesRowDto>> GetCustomerSalesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive);
    Task<IReadOnlyList<InventoryValuationRowDto>> GetInventoryValuationAsync(string locationCode = "DEFAULT");
    Task<IReadOnlyList<LowStockRowDto>> GetLowStockAsync(string locationCode, int lookbackDays, decimal suggestedReorderDays = 7m);
    Task<IReadOnlyList<TopProductRowDto>> GetTopProductsAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive, int topN = 15);
    Task<IReadOnlyList<ProfitByProductRowDto>> GetProfitByProductAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive, int topN = 50);
}

public sealed class ReportingService : IReportingService
{
    private readonly PosLocalDbContext _db;

    public ReportingService(PosLocalDbContext db) => _db = db;

    // ----------------------------
    // Helpers
    // ----------------------------
    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string FormatInches(int inches)
    {
        var ft = inches / 12;
        var rem = inches % 12;
        return $"{ft}ft {rem}in";
    }

    private static string FormatQty(LineQuantityKind kind, decimal qty, int inches)
        => kind == LineQuantityKind.Inches
            ? FormatInches(inches)
            : qty.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal QtyAsBase(LineQuantityKind kind, decimal qty, int inches)
        => kind == LineQuantityKind.Inches ? inches : qty;

    // ----------------------------
    // SALES
    // ----------------------------
    public async Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var rows = await _db.Sales.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= fromUtcInclusive && x.CreatedAtUtc < toUtcExclusive)
            .Select(x => new { x.NetTotal, x.VatTotal, x.GrossTotal })
            .ToListAsync();

        var count = rows.Count;
        var net = rows.Sum(x => x.NetTotal);
        var vat = rows.Sum(x => x.VatTotal);
        var gross = rows.Sum(x => x.GrossTotal);
        var avg = count == 0 ? 0m : gross / count;

        return new SalesSummaryDto(count, net, vat, gross, Money(avg));
    }

    public async Task<IReadOnlyList<SalesByDayRowDto>> GetSalesByDayAsync(
        DateTime fromUtcInclusive, DateTime toUtcExclusive, TimeZoneInfo tz)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= fromUtcInclusive && x.CreatedAtUtc < toUtcExclusive)
            .Select(x => new { x.CreatedAtUtc, x.NetTotal, x.VatTotal, x.GrossTotal })
            .ToListAsync();

        return sales
            .GroupBy(x =>
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc), tz)))
            .OrderBy(g => g.Key)
            .Select(g => new SalesByDayRowDto(
                g.Key,
                g.Count(),
                g.Sum(r => r.NetTotal),
                g.Sum(r => r.VatTotal),
                g.Sum(r => r.GrossTotal)))
            .ToList();
    }

    public async Task<IReadOnlyList<TopProductRowDto>> GetTopProductsAsync(
        DateTime fromUtcInclusive, DateTime toUtcExclusive, int topN = 15)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive),
                line => line.SaleId, sale => sale.Id, (line, sale) => line)
            .Join(_db.Products.AsNoTracking(),
                line => line.ProductId, p => p.Id,
                (line, p) => new
                {
                    p.Id, p.Sku, p.Name,
                    line.QuantityKind, line.Qty, line.QtyInches,
                    line.GrossTotal
                })
            .ToListAsync();

        return lines
            .GroupBy(x => new { x.Id, x.Sku, x.Name })
            .Select(g =>
            {
                var kind = g.Select(x => x.QuantityKind).FirstOrDefault();
                var qty = g.Sum(x => x.Qty);
                var inches = g.Sum(x => x.QtyInches);

                return new TopProductRowDto(
                    g.Key.Id,
                    g.Key.Sku,
                    g.Key.Name,
                    FormatQty(kind, qty, inches),
                    g.Sum(x => x.GrossTotal)
                );
            })
            .OrderByDescending(x => x.GrossTotal)
            .Take(Math.Max(1, topN))
            .ToList();
    }

    // ----------------------------
    // VAT
    // ----------------------------
    public async Task<VatSummaryDto> GetVatSummaryAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var rows = await _db.Sales.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= fromUtcInclusive && x.CreatedAtUtc < toUtcExclusive)
            .Select(x => new { x.NetTotal, x.VatTotal, x.GrossTotal })
            .ToListAsync();

        return new VatSummaryDto(
            rows.Sum(x => x.NetTotal),
            rows.Sum(x => x.VatTotal),
            rows.Sum(x => x.GrossTotal));
    }

    // ----------------------------
    // PROFIT (uses current Product.CostPrice)
    // NOTE: if you later want historical cost, we’ll store cost on SaleLine at sale time.
    // ----------------------------
    public async Task<ProfitSummaryDto> GetProfitSummaryAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive),
                l => l.SaleId, s => s.Id, (l, s) => l)
            .Join(_db.Products.AsNoTracking(),
                l => l.ProductId, p => p.Id,
                (l, p) => new
                {
                    l.QuantityKind, l.Qty, l.QtyInches,
                    l.GrossTotal,
                    p.CostPrice
                })
            .ToListAsync();

        var salesGross = lines.Sum(x => x.GrossTotal);
        var cogs = lines.Sum(x => QtyAsBase(x.QuantityKind, x.Qty, x.QtyInches) * x.CostPrice);
        var profit = salesGross - cogs;
        var margin = salesGross <= 0 ? 0m : (profit / salesGross) * 100m;

        return new ProfitSummaryDto(Money(salesGross), Money(cogs), Money(profit), Money(margin));
    }

    public async Task<IReadOnlyList<ProfitByProductRowDto>> GetProfitByProductAsync(
        DateTime fromUtcInclusive, DateTime toUtcExclusive, int topN = 50)
    {
        var lines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive),
                l => l.SaleId, s => s.Id, (l, s) => l)
            .Join(_db.Products.AsNoTracking(),
                l => l.ProductId, p => p.Id,
                (l, p) => new
                {
                    p.Id, p.Sku, p.Name, p.CostPrice,
                    l.QuantityKind, l.Qty, l.QtyInches,
                    l.GrossTotal
                })
            .ToListAsync();

        return lines
            .GroupBy(x => new { x.Id, x.Sku, x.Name, x.CostPrice })
            .Select(g =>
            {
                var kind = g.Select(x => x.QuantityKind).FirstOrDefault();
                var qty = g.Sum(x => x.Qty);
                var inches = g.Sum(x => x.QtyInches);
                var sales = g.Sum(x => x.GrossTotal);
                var cogs = g.Sum(x => QtyAsBase(x.QuantityKind, x.Qty, x.QtyInches) * x.CostPrice);
                var profit = sales - cogs;
                var margin = sales <= 0 ? 0m : (profit / sales) * 100m;

                return new ProfitByProductRowDto(
                    g.Key.Id,
                    g.Key.Sku,
                    g.Key.Name,
                    FormatQty(kind, qty, inches),
                    Money(sales),
                    Money(cogs),
                    Money(profit),
                    Money(margin)
                );
            })
            .OrderByDescending(x => x.GrossProfit)
            .Take(Math.Max(1, topN))
            .ToList();
    }

    // ----------------------------
    // TENDERS (reads Outbox sale payloads for method/change)
    // ----------------------------
    public async Task<TenderSummaryDto> GetTenderSummaryAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var saleIds = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive)
            .Select(s => s.Id)
            .ToListAsync();

        var sales = await _db.Sales.AsNoTracking()
            .Where(s => saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.GrossTotal, s.Status })
            .ToListAsync();

        // read outbox for the sale payloads (payment method & change)
        var outbox = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "sale" && saleIds.Contains(o.EntityId))
            .Select(o => new { o.EntityId, o.PayloadJson })
            .ToListAsync();

        var paymentMap = new Dictionary<Guid, (string method, decimal change)>();

        foreach (var o in outbox)
        {
            try
            {
                using var doc = JsonDocument.Parse(o.PayloadJson);
                if (!doc.RootElement.TryGetProperty("payment", out var p)) continue;

                var method = p.TryGetProperty("method", out var m) ? (m.GetString() ?? "") : "";
                var change = p.TryGetProperty("change", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : 0m;

                if (!string.IsNullOrWhiteSpace(method))
                    paymentMap[o.EntityId] = (method.Trim().ToUpperInvariant(), change);
            }
            catch
            {
                // ignore malformed outbox record
            }
        }

        decimal cash = 0, debit = 0, credit = 0, onacct = 0, changeTotal = 0;

        foreach (var s in sales)
        {
            // On-account: either status or method
            if (string.Equals(s.Status, "OnAccount", StringComparison.OrdinalIgnoreCase))
            {
                onacct += s.GrossTotal;
                continue;
            }

            if (paymentMap.TryGetValue(s.Id, out var pay))
            {
                changeTotal += pay.change;

                if (pay.method == "CASH") cash += s.GrossTotal;
                else if (pay.method == "DEBIT") debit += s.GrossTotal;
                else if (pay.method == "CREDIT") credit += s.GrossTotal;
                else cash += s.GrossTotal; // fallback
            }
            else
            {
                // fallback: treat as cash if missing
                cash += s.GrossTotal;
            }
        }

        var expectedCash = cash - changeTotal;

        return new TenderSummaryDto(
            Money(cash),
            Money(debit),
            Money(credit),
            Money(onacct),
            Money(changeTotal),
            Money(expectedCash)
        );
    }

    // ----------------------------
    // INVENTORY VALUATION
    // ----------------------------
    public async Task<IReadOnlyList<InventoryValuationRowDto>> GetInventoryValuationAsync(string locationCode = "DEFAULT")
    {
        locationCode = string.IsNullOrWhiteSpace(locationCode) ? "DEFAULT" : locationCode.Trim();

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Price, p.CostPrice, p.IsLength })
            .ToListAsync();

        var balances = await _db.Inventory.AsNoTracking()
            .Where(i => i.LocationCode == locationCode)
            .Select(i => new { i.ProductId, i.OnHand, i.OnHandInches })
            .ToListAsync();

        var balMap = balances.ToDictionary(x => x.ProductId, x => x);

        static string FormatOnHand(bool isLength, decimal onHand, int inches)
            => isLength ? FormatInches(inches) : onHand.ToString("0.##", CultureInfo.InvariantCulture);

        static decimal QtyBase(bool isLength, decimal onHand, int inches)
            => isLength ? inches : onHand;

        return products
            .Select(p =>
            {
                balMap.TryGetValue(p.Id, out var b);
                var onHand = b?.OnHand ?? 0m;
                var onHandInches = b?.OnHandInches ?? 0;

                var qtyBase = QtyBase(p.IsLength, onHand, onHandInches);
                var selling = qtyBase * p.Price;
                var cost = qtyBase * p.CostPrice;

                return new InventoryValuationRowDto(
                    p.Id,
                    p.Sku,
                    p.Name,
                    FormatOnHand(p.IsLength, onHand, onHandInches),
                    Money(selling),
                    Money(cost),
                    Money(selling - cost)
                );
            })
            .OrderByDescending(x => x.SellingValue)
            .ToList();
    }

    // ----------------------------
    // LOW STOCK / REORDER (avg usage)
    // Base units: Units or Inches (inches for length items)
    // ----------------------------
    public async Task<IReadOnlyList<LowStockRowDto>> GetLowStockAsync(
        string locationCode,
        int lookbackDays,
        decimal suggestedReorderDays = 7m)
    {
        locationCode = string.IsNullOrWhiteSpace(locationCode) ? "DEFAULT" : locationCode.Trim();
        lookbackDays = Math.Clamp(lookbackDays, 1, 90);

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-lookbackDays);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.Sku, p.Name, p.IsLength })
            .ToListAsync();

        var inv = await _db.Inventory.AsNoTracking()
            .Where(i => i.LocationCode == locationCode)
            .Select(i => new { i.ProductId, i.OnHand, i.OnHandInches })
            .ToListAsync();

        var usage = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc),
                l => l.SaleId, s => s.Id, (l, s) => l)
            .GroupBy(l => l.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                QtyUnits = g.Sum(x => x.Qty),
                QtyInches = g.Sum(x => x.QtyInches),
                Kind = g.Select(x => x.QuantityKind).FirstOrDefault()
            })
            .ToListAsync();

        var usageMap = usage.ToDictionary(x => x.ProductId, x => x);

        static string OnHandDisplay(bool isLength, decimal onHand, int inches)
            => isLength ? FormatInches(inches) : onHand.ToString("0.##", CultureInfo.InvariantCulture);

        static decimal OnHandBase(bool isLength, decimal onHand, int inches)
            => isLength ? inches : onHand;

        return products
            .Select(p =>
            {
                var b = inv.FirstOrDefault(x => x.ProductId == p.Id);
                var onHand = b?.OnHand ?? 0m;
                var onHandInches = b?.OnHandInches ?? 0;

                usageMap.TryGetValue(p.Id, out var u);

                // avg usage in base units per day
                decimal totalUsedBase = 0m;
                if (u != null)
                    totalUsedBase = u.Kind == LineQuantityKind.Inches ? u.QtyInches : u.QtyUnits;

                var avgDaily = lookbackDays == 0 ? 0m : totalUsedBase / lookbackDays;
                var onHandBase = OnHandBase(p.IsLength, onHand, onHandInches);

                var daysRemaining = avgDaily <= 0 ? 9999m : onHandBase / avgDaily;

                // suggested reorder = avgDaily * suggestedReorderDays (only if avgDaily > 0)
                var suggested = avgDaily <= 0 ? 0m : avgDaily * suggestedReorderDays;

                return new LowStockRowDto(
                    p.Id,
                    p.Sku,
                    p.Name,
                    OnHandDisplay(p.IsLength, onHand, onHandInches),
                    Money(avgDaily),
                    Money(daysRemaining),
                    Money(suggested)
                );
            })
            .OrderBy(x => x.DaysRemaining)
            .Take(200)
            .ToList();
    }

    // ----------------------------
    // INVENTORY MOVEMENT (Sales + Manual adjustments)
    // Manual adjustments come from Outbox: EntityType == "inventory_adjustment"
    // ----------------------------
    public async Task<IReadOnlyList<InventoryMovementRowDto>> GetInventoryMovementsAsync(
        DateTime fromUtcInclusive, DateTime toUtcExclusive, string locationCode = "DEFAULT")
    {
        locationCode = string.IsNullOrWhiteSpace(locationCode) ? "DEFAULT" : locationCode.Trim();

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToDictionaryAsync(p => p.Id);

        // 1) Sales movements (negative)
        var salesLines = await _db.SaleLines.AsNoTracking()
            .Join(_db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive),
                l => l.SaleId, s => s.Id,
                (l, s) => new { s.CreatedAtUtc, s.ReceiptNo, l.ProductId, l.QuantityKind, l.Qty, l.QtyInches })
            .ToListAsync();

        var saleMoves = new List<InventoryMovementRowDto>();

        foreach (var x in salesLines)
        {
            if (!products.TryGetValue(x.ProductId, out var p))
                continue;

            var deltaDisplay = x.QuantityKind == LineQuantityKind.Inches
                ? $"-{FormatInches(x.QtyInches)}"
                : $"-{x.Qty:0.##}";

            saleMoves.Add(new InventoryMovementRowDto(
                x.CreatedAtUtc,
                "SALE",
                p.Sku,
                p.Name,
                deltaDisplay,
                $"Receipt {x.ReceiptNo}"
            ));
        }

        // 2) Manual adjustments (from Outbox)
        var adjustments = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "inventory_adjustment"
                        && o.CreatedAtUtc >= fromUtcInclusive
                        && o.CreatedAtUtc < toUtcExclusive)
            .Select(o => new { o.CreatedAtUtc, o.PayloadJson })
            .ToListAsync();

        var adjustMoves = new List<InventoryMovementRowDto>();

        foreach (var a in adjustments)
        {
            try
            {
                using var doc = JsonDocument.Parse(a.PayloadJson);
                var root = doc.RootElement;

                var loc = root.TryGetProperty("location_code", out var lc) ? (lc.GetString() ?? "DEFAULT") : "DEFAULT";
                if (!string.Equals(loc, locationCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pid = root.TryGetProperty("product_id", out var p) ? p.GetGuid() : Guid.Empty;
                var delta = root.TryGetProperty("delta", out var d) ? d.GetDecimal() : 0m;
                var reason = root.TryGetProperty("reason", out var r) ? (r.GetString() ?? "Manual adjustment") : "Manual adjustment";

                if (pid == Guid.Empty) continue;
                if (!products.TryGetValue(pid, out var prod))
                    continue;

                var deltaDisplay = delta >= 0 ? $"+{delta:0.###}" : $"{delta:0.###}";

                adjustMoves.Add(new InventoryMovementRowDto(
                    a.CreatedAtUtc,
                    "ADJUST",
                    prod.Sku,
                    prod.Name,
                    deltaDisplay,
                    reason
                ));
            }
            catch
            {
                // ignore malformed adjustment record
            }
        }

        return saleMoves
            .Concat(adjustMoves)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(500)
            .ToList();
    }


    // ----------------------------
    // CUSTOMER SALES
    // ----------------------------
    public async Task<IReadOnlyList<CustomerSalesRowDto>> GetCustomerSalesAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive && s.CustomerId != null)
            .Select(s => new { s.CustomerId, s.GrossTotal })
            .ToListAsync();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.DeletedAtUtc == null)
            .Select(c => new { c.Id, c.Name, c.Balance })
            .ToListAsync();

        var custMap = customers.ToDictionary(x => x.Id, x => x);

        return sales
            .GroupBy(x => x.CustomerId!.Value)
            .Select(g =>
            {
                custMap.TryGetValue(g.Key, out var c);
                return new CustomerSalesRowDto(
                    g.Key,
                    c?.Name ?? "Unknown",
                    g.Count(),
                    Money(g.Sum(x => x.GrossTotal)),
                    Money(c?.Balance ?? 0m)
                );
            })
            .OrderByDescending(x => x.GrossTotal)
            .Take(200)
            .ToList();
    }
    
    // ----------------------------
    // EXPORT HELPERS
    // ----------------------------
    public async Task<IReadOnlyList<SalesExportRowDto>> GetSalesExportAsync(DateTime fromUtcInclusive, DateTime toUtcExclusive)
    {
        var sales = await _db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtcInclusive && s.CreatedAtUtc < toUtcExclusive)
            .Select(s => new { s.Id, s.CreatedAtUtc, s.ReceiptNo, s.Status, s.CustomerId, s.NetTotal, s.VatTotal, s.GrossTotal })
            .ToListAsync();

        var customerIds = sales
            .Where(s => s.CustomerId.HasValue)
            .Select(s => s.CustomerId!.Value)
            .Distinct()
            .ToList();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        var customerMap = customers.ToDictionary(c => c.Id, c => c.Name);

        return sales
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SalesExportRowDto(
                s.CreatedAtUtc,
                s.ReceiptNo,
                s.Status,
                s.CustomerId.HasValue && customerMap.TryGetValue(s.CustomerId.Value, out var name) ? name : "Walk-in",
                Money(s.NetTotal),
                Money(s.VatTotal),
                Money(s.GrossTotal)
            ))
            .ToList();
    }


    public async Task<IReadOnlyList<PurchaseExportRowDto>> GetPurchaseAdjustmentsAsync(
        DateTime fromUtcInclusive,
        DateTime toUtcExclusive,
        string locationCode = "DEFAULT")
    {
        locationCode = string.IsNullOrWhiteSpace(locationCode) ? "DEFAULT" : locationCode.Trim();

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToDictionaryAsync(p => p.Id);

        var adjustments = await _db.Outbox.AsNoTracking()
            .Where(o => o.EntityType == "inventory_adjustment"
                        && o.CreatedAtUtc >= fromUtcInclusive
                        && o.CreatedAtUtc < toUtcExclusive)
            .Select(o => new { o.CreatedAtUtc, o.PayloadJson })
            .ToListAsync();

        var rows = new List<PurchaseExportRowDto>();

        foreach (var a in adjustments)
        {
            try
            {
                using var doc = JsonDocument.Parse(a.PayloadJson);
                var root = doc.RootElement;

                var loc = root.TryGetProperty("location_code", out var lc) ? (lc.GetString() ?? "DEFAULT") : "DEFAULT";
                if (!string.Equals(loc, locationCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pid = root.TryGetProperty("product_id", out var p) ? p.GetGuid() : Guid.Empty;
                var delta = root.TryGetProperty("delta", out var d) ? d.GetDecimal() : 0m;
                var reason = root.TryGetProperty("reason", out var r) ? (r.GetString() ?? "Purchase") : "Purchase";

                if (pid == Guid.Empty || delta <= 0m) continue;
                if (!products.TryGetValue(pid, out var prod))
                    continue;

                rows.Add(new PurchaseExportRowDto(
                    a.CreatedAtUtc,
                    prod.Sku,
                    prod.Name,
                    delta.ToString("0.###", CultureInfo.InvariantCulture),
                    reason
                ));
            }
            catch
            {
                // ignore malformed adjustment record
            }
        }

        return rows
            .OrderByDescending(r => r.OccurredAtUtc)
            .ToList();
    }
}
