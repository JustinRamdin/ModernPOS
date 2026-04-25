using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;
using Pos.Contracts;
using System.Text.Json;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly PosDbContext _db;
    public SalesController(PosDbContext db) => _db = db;

     [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<ServerSalesExportRowDto>>> Export([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
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
    
    [HttpPost("checkout")]
    public async Task<ActionResult<object>> Checkout([FromBody] CheckoutRequest req)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.SuperUser)) return Unauthorized();
        if (req.Lines is null || req.Lines.Count == 0)
            return BadRequest("No lines.");

        if (req.Payments is null || req.Payments.Count == 0)
            return BadRequest("No payments.");

        // Load products and calculate totals
        var productIds = req.Lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var line in req.Lines)
            if (!products.ContainsKey(line.ProductId))
                return BadRequest($"Unknown product: {line.ProductId}");

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            TerminalId = req.TerminalId ?? "",
            SoldAtUtc = DateTime.UtcNow
        };

        foreach (var line in req.Lines)
        {
            var p = products[line.ProductId];
            var unit = p.Price;
            var total = Math.Round(unit * line.Qty, 2);

            sale.Lines.Add(new SaleLine
            {
                Id = Guid.NewGuid(),
                SaleId = sale.Id,
                ProductId = p.Id,
                Qty = line.Qty,
                UnitPrice = unit,
                LineTotal = total
            });
        }

        sale.Subtotal = sale.Lines.Sum(x => x.LineTotal);
        sale.Total = sale.Subtotal; // later add tax/discount rules here

        foreach (var pay in req.Payments)
        {
            sale.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                SaleId = sale.Id,
                Method = (PaymentMethod)pay.Method,
                Amount = pay.Amount
            });
        }

        var paid = sale.Payments.Sum(x => x.Amount);
        if (paid < sale.Total)
            return BadRequest($"Insufficient payment. Total={sale.Total}, Paid={paid}");

        _db.Sales.Add(sale);

        // Outbox event for future sync
        var payload = JsonSerializer.Serialize(new { saleId = sale.Id, terminalId = sale.TerminalId, soldAtUtc = sale.SoldAtUtc });
        _db.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            Type = "SaleCreated",
            PayloadJson = payload,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Ok(new { saleId = sale.Id, total = sale.Total, paid, change = paid - sale.Total });
    }
}
