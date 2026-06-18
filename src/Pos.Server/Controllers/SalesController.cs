using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;
using Pos.Contracts;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly PosDbContext _db;
     private readonly ILogger<SalesController> _logger;

     public SalesController(PosDbContext db, ILogger<SalesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("export")]
    public async Task<ActionResult<IReadOnlyList<ServerSalesExportRowDto>>> Export([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
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
    
    [HttpPost("checkout")]
    public async Task<ActionResult<object>> Checkout([FromBody] CheckoutRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.SuperUser)) return Unauthorized();
        if (req.Lines is null || req.Lines.Count == 0)
            return BadRequest("No lines.");

        if (req.Payments is null || req.Payments.Count == 0)
            return BadRequest("No payments.");

        if (req.CustomerId is not null && !await _db.Customers.AnyAsync(c => c.Id == req.CustomerId.Value && c.IsActive, ct))
            return BadRequest("Selected customer was not found.");

        var isOnAccount = req.Payments.Any(p => (PaymentMethod)p.Method == PaymentMethod.OnAccount);
        if (isOnAccount && req.CustomerId is null)
            return BadRequest("Customer is required for on-account checkout.");

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
             // Load products once for pricing/validation.
            var productIds = req.Lines.Select(x => x.ProductId).Distinct().ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToDictionaryAsync(p => p.Id, ct);

            foreach (var line in req.Lines)
            {
                if (!products.ContainsKey(line.ProductId))
                    return BadRequest($"Unknown or inactive product: {line.ProductId}");

            if (line.Qty <= 0)
                    return BadRequest("Line quantity must be greater than zero.");
            }

            if (products.Values.Select(p => p.InventoryBucket).Distinct().Count() > 1)
                return BadRequest("Items from Inventory 1 and Inventory 2 cannot be mixed in one sale.");

            var sale = new Sale
            {
                Id = Guid.NewGuid(),
                TerminalId = req.TerminalId ?? "",
                CustomerId = req.CustomerId,
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

                var unitAdjustments = req.Lines
                .Where(l => !products[l.ProductId].IsLength)
                .GroupBy(l => l.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = Math.Round(g.Sum(x => x.Qty), 3) })
                .ToList();

                var lengthAdjustments = req.Lines
                .Where(l => products[l.ProductId].IsLength)
                .GroupBy(l => l.ProductId)
                .Select(g => new { ProductId = g.Key, Inches = g.Sum(x => x.Qty) })
                .ToList();

                foreach (var adj in unitAdjustments)
            {
                var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE Products
SET OnHand = ROUND(OnHand - {adj.Qty}, 3)
WHERE Id = {adj.ProductId}
  AND IsLength = 0
  AND IsActive = 1
  AND OnHand >= {adj.Qty};", ct);

                if (affected != 1)
                {
                    var p = products[adj.ProductId];
                    _logger.LogWarning("Stock deduction failed for unit product {ProductId} ({ProductName}). RequestedQty={Qty}", adj.ProductId, p.Name, adj.Qty);
                    return BadRequest($"{p.Name}: Insufficient stock or concurrent update detected.");
                }

                _logger.LogInformation("Stock deducted for unit product {ProductId}. Qty={Qty}", adj.ProductId, adj.Qty);
            }
           foreach (var adj in lengthAdjustments)
            {
                if (adj.Inches != decimal.Truncate(adj.Inches))
                {
                    var p = products[adj.ProductId];
                    return BadRequest($"{p.Name}: Length quantity must be a whole number of inches.");
                }

                if (adj.Inches > int.MaxValue || adj.Inches < int.MinValue)
                {
                    var p = products[adj.ProductId];
                    return BadRequest($"{p.Name}: Length quantity is out of range.");
                }

                var inchesToSubtract = (int)adj.Inches;
                var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE Products
SET OnHandInches = OnHandInches - {inchesToSubtract}
WHERE Id = {adj.ProductId}
  AND IsLength = 1
  AND IsActive = 1
  AND OnHandInches >= {inchesToSubtract};", ct);

                if (affected != 1)
                {
                    var p = products[adj.ProductId];
                    _logger.LogWarning("Stock deduction failed for length product {ProductId} ({ProductName}). RequestedInches={Inches}", adj.ProductId, p.Name, inchesToSubtract);
                    return BadRequest($"{p.Name}: Insufficient stock or concurrent update detected.");
                }

                _logger.LogInformation("Stock deducted for length product {ProductId}. Inches={Inches}", adj.ProductId, inchesToSubtract);
            }

            sale.Subtotal = Math.Round(sale.Lines.Sum(x => x.LineTotal), 2, MidpointRounding.AwayFromZero);
            var discountAmount = Math.Round(Math.Max(0m, req.DiscountAmount), 2, MidpointRounding.AwayFromZero);
            if (discountAmount > sale.Subtotal)
                return BadRequest("Discount cannot be greater than subtotal.");

            sale.Total = Math.Round(sale.Subtotal - discountAmount, 2, MidpointRounding.AwayFromZero);


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
            if (!isOnAccount && paid < sale.Total)
                return BadRequest($"Insufficient payment. Total={sale.Total}, Paid={paid}");

            if (isOnAccount)
            {
                foreach (var payment in sale.Payments.Where(payment => payment.Method == PaymentMethod.OnAccount))
                    payment.Amount = sale.Total;

                var customer = await _db.Customers.FirstAsync(c => c.Id == req.CustomerId!.Value, ct);
                customer.Balance = Math.Round(customer.Balance + sale.Total, 2, MidpointRounding.AwayFromZero);
                customer.UpdatedAtUtc = DateTime.UtcNow;
                paid = sale.Total;
            }

            _db.Sales.Add(sale);

            var payload = JsonSerializer.Serialize(new { saleId = sale.Id, terminalId = sale.TerminalId, soldAtUtc = sale.SoldAtUtc });
            _db.OutboxEvents.Add(new OutboxEvent
            {
                Id = Guid.NewGuid(),
                Type = "SaleCreated",
                PayloadJson = payload,
                CreatedAtUtc = DateTime.UtcNow
            });

        await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

       _logger.LogInformation("Checkout committed. SaleId={SaleId}, Terminal={TerminalId}, Lines={LineCount}, Total={Total}", sale.Id, sale.TerminalId, sale.Lines.Count, sale.Total);
            return Ok(new { saleId = sale.Id, total = sale.Total, paid, change = paid - sale.Total });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Checkout failed and was rolled back.");
            throw;
        }
    }
}
