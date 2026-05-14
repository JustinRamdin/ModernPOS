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
