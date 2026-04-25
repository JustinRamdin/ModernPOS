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

        var sales = await _db.Sales.AsNoTracking().Where(s => s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc).ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();
        var lines = saleIds.Count == 0
            ? []
            : await _db.SaleLines.AsNoTracking().Where(l => saleIds.Contains(l.SaleId)).ToListAsync(ct);
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

        var salesByCustomerId = sales
            .Where(s => s.CustomerId.HasValue)
            .GroupBy(s => s.CustomerId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ReceiptCount = g.Count(),
                    SalesGross = g.Sum(s => s.Total)
                });

        var customerEntities = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var customers = customerEntities
            .Select(c =>
            {
                salesByCustomerId.TryGetValue(c.Id, out var customerSales);
                return new CustomerSalesRowDto(
                    c.Name,
                    customerSales?.ReceiptCount ?? 0,
                    customerSales?.SalesGross ?? 0m,
                    c.Balance);
            })
            .ToList();
        var gross = sales.Sum(x => x.Total);
        var salesGross = lines.Sum(x => x.LineTotal);
        var cogsTotal = lineGroups.Sum(x => x.cogs);

        return new ReportSummaryDto(sales.Count, gross, salesGross, cogsTotal, salesGross - cogsTotal, salesByDay, topProducts, profitByProduct, inventory, customers);
    }
}
