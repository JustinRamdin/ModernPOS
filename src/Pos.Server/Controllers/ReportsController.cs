using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public async Task<ActionResult<ReportSummaryDto>> Summary([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        // Keep report calculations aligned with the Sales Register dataset.
        var sales = await _db.Sales
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .ToListAsync(ct);

        var lines = sales.SelectMany(s => s.Lines).ToList();
        var products = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        var salesByDay = sales.GroupBy(s => DateOnly.FromDateTime(s.SoldAtUtc.Date)).OrderBy(g => g.Key)
            .Select(g => new SalesByDayRowDto(g.Key, g.Count(), g.Sum(x => x.Total))).ToList();

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

        var inventory = (await _db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct))
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

        // Sales are not linked to customers yet, so attribute all register sales to an "Unassigned" bucket
        // to keep Customer Sales aligned with the same source data used by Sales Register.
        var totalCustomerReceipts = sales.Count;
        var totalCustomerGross = sales.Sum(s => s.Total);
        var customers = customerEntities
            .Select(c => new CustomerSalesRowDto(
                c.Name,
                0,
                0m,
                c.Balance))
            .ToList();

            if (totalCustomerReceipts > 0 || totalCustomerGross > 0m)
        {
            customers.Insert(0, new CustomerSalesRowDto("Unassigned", totalCustomerReceipts, totalCustomerGross, 0m));
        }
        var gross = sales.Sum(x => x.Total);
        var salesGross = lines.Sum(x => x.LineTotal);
        var cogsTotal = lineGroups.Sum(x => x.cogs);

        return new ReportSummaryDto(sales.Count, gross, salesGross, cogsTotal, salesGross - cogsTotal, salesByDay, topProducts, profitByProduct, inventory, customers);
    }

    [HttpGet("sales-log")]
    public async Task<ActionResult<IReadOnlyList<SaleLogEntryDto>>> SalesLog([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Payments)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .OrderByDescending(s => s.SoldAtUtc)
            .ToListAsync(ct);

        return sales.Select(s => new SaleLogEntryDto(
            s.Id,
            s.SoldAtUtc,
            s.Id.ToString("N")[..8].ToUpperInvariant(),
            s.Subtotal,
            s.Total,
            s.Payments.Count == 0 ? "Unknown" : string.Join(", ", s.Payments.Select(p => p.Method.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)),
            s.Lines.Select(l => new SaleLogLineDto(l.Id, l.ProductId, l.Product?.Name ?? "Unknown", l.Qty, l.UnitPrice, l.LineTotal)).ToList()
        )).ToList();
    }

    [HttpGet("inventory-movements")]
    public async Task<ActionResult<IReadOnlyList<InventoryMovementRowDto>>> InventoryMovements([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] string? locationCode, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .OrderByDescending(s => s.SoldAtUtc)
            .ToListAsync(ct);

        var rows = sales.SelectMany(s => s.Lines.Select(l =>
        {
            var isLength = l.Product?.IsLength ?? false;
            var qtyText = isLength ? $"-{Math.Round(l.Qty * 12m, 0):0} in" : $"-{l.Qty:0.##}";
            return new InventoryMovementRowDto(s.SoldAtUtc, "SALE", l.Product?.Sku ?? string.Empty, l.Product?.Name ?? "Unknown", qtyText, $"Receipt {s.Id.ToString("N")[..8].ToUpperInvariant()}");
        })).ToList();

        return rows;
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<LowStockRowDto>>> LowStock([FromQuery] string? locationCode, [FromQuery] int lookbackDays = 14, CancellationToken ct = default)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var fromUtc = DateTime.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var lineUsage = await _db.SaleLines.AsNoTracking()
            .Include(l => l.Sale)
            .Where(l => l.Sale != null && l.Sale.SoldAtUtc >= fromUtc)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
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


    [HttpPost("sales/{saleId:guid}/refund-item")]
    public async Task<ActionResult> RefundSaleItem(Guid saleId, [FromBody] SaleItemRefundRequest request, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();

        var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == saleId, ct);
        if (sale is null) return NotFound();

        var line = sale.Lines.FirstOrDefault(l => l.Id == request.SaleLineId);
        if (line is null) return NotFound();
        if (request.Quantity <= 0 || request.Quantity > line.Qty) return BadRequest("Invalid refund quantity.");

        var refundLineTotal = Math.Round(line.UnitPrice * request.Quantity, 2, MidpointRounding.AwayFromZero);
        var refundSale = new Sale
        {
            Id = Guid.NewGuid(),
            TerminalId = sale.TerminalId,
            SoldAtUtc = DateTime.UtcNow,
            Subtotal = -refundLineTotal,
            Total = -refundLineTotal,
            Lines = new List<SaleLine>
            {
                new()
                {
                    ProductId = line.ProductId,
                    Qty = -request.Quantity,
                    UnitPrice = line.UnitPrice,
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
        return Ok();
    }

     [HttpGet("sales-export")]
    public async Task<ActionResult<IReadOnlyList<ServerSalesExportRowDto>>> SalesExport([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.Accountant, UserRole.SuperUser, UserRole.Cashier)) return Unauthorized();

        var sales = await _db.Sales
            .AsNoTracking()
            .Include(s => s.Payments)
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
                string.Empty,
                sale.Subtotal,
                0m,
                sale.Total);
        }).ToList();

        return rows;
    }
}
