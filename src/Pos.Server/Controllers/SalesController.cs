using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;
using Pos.Server.Contracts;
using System.Text.Json;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly PosDbContext _db;
    public SalesController(PosDbContext db) => _db = db;

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
