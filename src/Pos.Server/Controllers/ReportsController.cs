using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Pos.Contracts;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly PosDbContext _db;
    public ReportsController(PosDbContext db) => _db = db;

    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> Summary([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] int? inventoryBucket, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        // Keep report calculations aligned with the Sales Register dataset.
        var sales = await _db.Sales
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .ToListAsync(ct);

        var products = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var bucket = NormalizeInventoryBucket(inventoryBucket);
        var lines = sales.SelectMany(s => s.Lines)
            .Where(l => bucket is null || (products.TryGetValue(l.ProductId, out var p) && p.InventoryBucket == bucket.Value))
            .ToList();
        var salesForBucket = bucket is null
            ? sales
            : sales.Where(s => s.Lines.Any(l => products.TryGetValue(l.ProductId, out var p) && p.InventoryBucket == bucket.Value)).ToList();

        var salesByDay = lines.GroupBy(l => DateOnly.FromDateTime(l.Sale?.SoldAtUtc.Date ?? fromUtc.Date)).OrderBy(g => g.Key)
            .Select(g => new SalesByDayRowDto(g.Key, g.Select(x => x.SaleId).Distinct().Count(), g.Sum(x => x.LineTotal))).ToList();

        var lineGroups = lines.GroupBy(l => l.ProductId)
            .Select(g =>
            {
                products.TryGetValue(g.Key, out var p);
                var qty = g.Sum(x => x.Qty);
                var revenue = g.Sum(x => x.LineTotal);
                var cogs = (p?.CostPrice ?? 0m) * qty;
                return new { p, qty, revenue, cogs };
            }).ToList();

        var topProducts = lineGroups.OrderByDescending(x => x.revenue).Take(15)
            .Select(x => new TopProductRowDto(x.p?.Name ?? "Unknown", x.p?.Sku, x.qty, x.revenue)).ToList();

        var profitByProduct = lineGroups.OrderByDescending(x => x.revenue - x.cogs).Take(50)
            .Select(x => new ProfitByProductRowDto(x.p?.Name ?? "Unknown", x.p?.Sku, x.qty, x.revenue, x.cogs, x.revenue - x.cogs)).ToList();

        var inventory = (await _db.Products.AsNoTracking().Where(p => p.IsActive && (bucket == null || p.InventoryBucket == bucket.Value)).OrderBy(p => p.Name).ToListAsync(ct))
           .Select(p =>
            {
                var quantity = p.IsLength ? p.OnHandInches : p.OnHand;
                var costValue = p.CostPrice * quantity;
                return new InventoryValuationRowDto(p.Name, p.Sku, p.OnHand, p.OnHandInches, p.CostPrice, p.Price, costValue);
            })
            .ToList();

        var customerEntities = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var salesByCustomer = salesForBucket
            .Where(s => s.CustomerId is not null)
            .GroupBy(s => s.CustomerId!.Value)
            .ToDictionary(g => g.Key, g => new { ReceiptCount = g.Count(), GrossTotal = g.Sum(s => s.Total) });

        var customers = customerEntities
            .Select(c =>
            {
                salesByCustomer.TryGetValue(c.Id, out var customerSales);
                return new CustomerSalesRowDto(
                    c.Name,
                    customerSales?.ReceiptCount ?? 0,
                    customerSales?.GrossTotal ?? 0m,
                    c.Balance);
            })
            .ToList();

        var unassignedSales = salesForBucket.Where(s => s.CustomerId is null).ToList();
        if (unassignedSales.Count > 0)
        {
            customers.Insert(0, new CustomerSalesRowDto("Unassigned", unassignedSales.Count, unassignedSales.Sum(s => s.Total), 0m));
        }
        var gross = lines.Sum(x => x.LineTotal);
        var salesGross = lines.Sum(x => x.LineTotal);
        var cogsTotal = lineGroups.Sum(x => x.cogs);

        return new ReportSummaryDto(salesForBucket.Count, gross, salesGross, cogsTotal, salesGross - cogsTotal, salesByDay, topProducts, profitByProduct, inventory, customers);
    }

    [HttpGet("sales-log")]
    public async Task<ActionResult<IReadOnlyList<SaleLogEntryDto>>> SalesLog([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] int? inventoryBucket, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Payments)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .OrderByDescending(s => s.SoldAtUtc)
            .ToListAsync(ct);
        var bucket = NormalizeInventoryBucket(inventoryBucket);
        if (bucket is not null)
        {
            sales = sales
                .Where(s => s.Lines.Any(l => l.Product?.InventoryBucket == bucket.Value))
                .ToList();
        }

        var saleLineIds = sales.SelectMany(s => s.Lines).Select(l => l.Id).ToList();
        var refundQuantities = (await _db.SaleLines.AsNoTracking()
                .Where(l => l.RefundedFromSaleLineId != null && saleLineIds.Contains(l.RefundedFromSaleLineId.Value))
                .Select(l => new { SourceId = l.RefundedFromSaleLineId!.Value, l.Qty })
                .ToListAsync(ct))
            .GroupBy(x => x.SourceId)
            .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(x => x.Qty)));

        return sales.Select(s => new SaleLogEntryDto(
            s.Id,
            s.SoldAtUtc,
            s.Id.ToString("N")[..8].ToUpperInvariant(),
            s.Subtotal,
            s.Total,
            s.Payments.Count == 0 ? "Unknown" : string.Join(", ", s.Payments.Select(p => p.Method.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)),
            s.Lines.Where(l => bucket is null || l.Product?.InventoryBucket == bucket.Value).Select(l => new SaleLogLineDto(
                l.Id, l.ProductId, l.Product?.Name ?? "Unknown", l.Qty, l.UnitPrice, l.LineTotal, l.VatTotal,
                refundQuantities.GetValueOrDefault(l.Id))).ToList(),
            s.VatTotal
        )).ToList();
    }

    [HttpGet("customer-receivables")]
    public async Task<ActionResult<IReadOnlyList<CustomerReceivablesRowDto>>> CustomerReceivables([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var onAccountSales = await _db.Sales.AsNoTracking()
            .Include(s => s.Payments)
            .Where(s => s.CustomerId != null && s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .Where(s => s.Payments.Any(p => p.Method == PaymentMethod.OnAccount))
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, ReceiptCount = g.Count(), Receivables = g.Sum(s => s.Total) })
            .ToListAsync(ct);

        var payments = await _db.CustomerPayments.AsNoTracking()
            .Where(p => p.PaidAtUtc >= fromUtc && p.PaidAtUtc < toUtc)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, PaymentsMade = g.Sum(p => p.Amount) })
            .ToListAsync(ct);

        var salesByCustomer = onAccountSales.ToDictionary(x => x.CustomerId);
        var paymentsByCustomer = payments.ToDictionary(x => x.CustomerId);

        return customers
            .Select(c =>
            {
                salesByCustomer.TryGetValue(c.Id, out var sales);
                paymentsByCustomer.TryGetValue(c.Id, out var payment);
                var receivables = sales?.Receivables ?? 0m;
                var paymentsMade = payment?.PaymentsMade ?? 0m;
                return new CustomerReceivablesRowDto(
                    c.Name,
                    sales?.ReceiptCount ?? 0,
                    receivables,
                    paymentsMade,
                    receivables - paymentsMade);
            })
            .Where(r => r.Receivables != 0m || r.PaymentsMade != 0m || r.RemainingBalance != 0m)
            .ToList();
    }

    [HttpGet("inventory-movements")]
    public async Task<ActionResult<IReadOnlyList<InventoryMovementRowDto>>> InventoryMovements([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] string? locationCode, [FromQuery] int? inventoryBucket, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .OrderByDescending(s => s.SoldAtUtc)
            .ToListAsync(ct);

        var bucket = NormalizeInventoryBucket(inventoryBucket);
        var rows = sales.SelectMany(s => s.Lines.Where(l => bucket is null || l.Product?.InventoryBucket == bucket.Value).Select(l =>
        {
            var isLength = l.Product?.IsLength ?? false;
            var qtyText = isLength ? $"-{Math.Round(l.Qty * 12m, 0):0} in" : $"-{l.Qty:0.##}";
            return new InventoryMovementRowDto(s.SoldAtUtc, "SALE", l.Product?.Sku ?? string.Empty, l.Product?.Name ?? "Unknown", qtyText, $"Receipt {s.Id.ToString("N")[..8].ToUpperInvariant()}");
        })).ToList();

        return rows;
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<LowStockRowDto>>> LowStock([FromQuery] string? locationCode, [FromQuery] int lookbackDays = 14, [FromQuery] int? inventoryBucket = null, CancellationToken ct = default)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var fromUtc = DateTime.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var lineUsage = await _db.SaleLines.AsNoTracking()
            .Include(l => l.Sale)
            .Where(l => l.Sale != null && l.Sale.SoldAtUtc >= fromUtc)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        var bucket = NormalizeInventoryBucket(inventoryBucket);
        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive && (bucket == null || p.InventoryBucket == bucket.Value)).OrderBy(p => p.Name).ToListAsync(ct);
        var rows = products.Select(p =>
        {
            var usage = lineUsage.TryGetValue(p.Id, out var qty) ? qty / Math.Max(1, lookbackDays) : 0m;
            var onHandBase = p.IsLength ? p.OnHandInches : p.OnHand;
            var daysRemaining = usage <= 0 ? 9999m : onHandBase / usage;
            var reorder = Math.Max(0, (usage * 14m) - onHandBase);
            return new LowStockRowDto(p.Sku, p.Name, p.IsLength ? $"{p.OnHandInches} in" : p.OnHand.ToString("0.##"), Math.Round(usage,2), Math.Round(daysRemaining,1), Math.Round(reorder,2));
        }).Where(r => r.DaysRemaining <= 14m).ToList();

        return rows;
    }

    private static int? NormalizeInventoryBucket(int? inventoryBucket)
        => inventoryBucket is null ? null : Math.Clamp(inventoryBucket.Value, 1, 2);


    [HttpPost("sales/{saleId:guid}/refund-item")]
    public async Task<ActionResult> RefundSaleItem(Guid saleId, [FromBody] SaleItemRefundRequest request, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == saleId, ct);
        if (sale is null) return NotFound();

        var line = sale.Lines.FirstOrDefault(l => l.Id == request.SaleLineId);
        if (line is null) return NotFound();
        var priorRefundQuantities = await _db.SaleLines
            .Where(l => l.RefundedFromSaleLineId == line.Id)
            .Select(l => l.Qty)
            .ToListAsync(ct);
        var alreadyRefunded = Math.Abs(priorRefundQuantities.Sum());
        var remainingQuantity = Math.Max(0m, line.Qty - alreadyRefunded);
        if (request.Quantity <= 0 || request.Quantity > remainingQuantity)
            return BadRequest($"Invalid refund quantity. Only {remainingQuantity:0.###} remains refundable.");

        if (line.ProductId != CheckoutSpecialProducts.MiscellaneousId)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ProductId, ct);
            if (product is null)
                return BadRequest("The refunded product no longer exists in inventory.");

            if (product.IsLength)
            {
                if (request.Quantity != decimal.Truncate(request.Quantity) || request.Quantity > int.MaxValue)
                    return BadRequest("Length refund quantity must be a whole number of inches.");

                product.OnHandInches = checked(product.OnHandInches + (int)request.Quantity);
            }
            else
            {
                product.OnHand = Math.Round(product.OnHand + request.Quantity, 3, MidpointRounding.AwayFromZero);
            }
        }

        var refundRatio = request.Quantity / line.Qty;
        decimal refundNetTotal;
        decimal refundVatTotal;
        decimal refundLineTotal;

        if (line.VatTotal != 0m)
        {
            // Current sales store gross and VAT on every line.
            refundLineTotal = Math.Round(line.LineTotal * refundRatio, 2, MidpointRounding.AwayFromZero);
            refundVatTotal = Math.Round(line.VatTotal * refundRatio, 2, MidpointRounding.AwayFromZero);
            refundNetTotal = Math.Round(refundLineTotal - refundVatTotal, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            // Compatibility for sales created before line-level VAT was stored.
            // Their line totals are the entered net amounts, while VAT exists only on the sale.
            var saleLineBase = sale.Lines.Where(l => l.Qty > 0m).Sum(l => Math.Abs(l.LineTotal));
            var allocatedLineVat = saleLineBase == 0m
                ? 0m
                : Math.Round(sale.VatTotal * (Math.Abs(line.LineTotal) / saleLineBase), 2, MidpointRounding.AwayFromZero);

            refundNetTotal = Math.Round(line.LineTotal * refundRatio, 2, MidpointRounding.AwayFromZero);
            refundVatTotal = Math.Round(allocatedLineVat * refundRatio, 2, MidpointRounding.AwayFromZero);
            refundLineTotal = Math.Round(refundNetTotal + refundVatTotal, 2, MidpointRounding.AwayFromZero);
        }
        var refundSale = new Sale
        {
            Id = Guid.NewGuid(),
            TerminalId = sale.TerminalId,
            CustomerId = sale.CustomerId,
            SoldAtUtc = DateTime.UtcNow,
            Subtotal = -refundNetTotal,
            VatTotal = -refundVatTotal,
            Total = -refundLineTotal,
            Lines = new List<SaleLine>
            {
                new()
                {
                    ProductId = line.ProductId,
                    RefundedFromSaleLineId = line.Id,
                    Qty = -request.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatTotal = -refundVatTotal,
                    LineTotal = -refundLineTotal
                }
            },
            Payments = new List<Payment>
            {
                new() { Method = PaymentMethod.Cash, Amount = -refundLineTotal }
            }
        };

        _db.Sales.Add(refundSale);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok();
    }

     [HttpGet("sales-export")]
    public async Task<ActionResult<IReadOnlyList<ServerSalesExportRowDto>>> SalesExport([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales
            .AsNoTracking()
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .OrderByDescending(s => s.SoldAtUtc)
            .ToListAsync(ct);

        var rows = sales.Select(sale =>
        {
            var paymentType = sale.Payments.Count == 0
                ? null
                : string.Join(", ", sale.Payments
                    .Select(payment => payment.Method.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            return new ServerSalesExportRowDto(
                sale.SoldAtUtc,
                sale.Id.ToString("N")[..8].ToUpperInvariant(),
                "Completed",
                paymentType,
                sale.Customer?.Name ?? string.Empty,
                sale.Subtotal,
                0m,
                sale.Total);
        }).ToList();

        return rows;
    }
}
