using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Contracts;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly PosDbContext _db;
    public CustomersController(PosDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<CustomerDto>>> Get(CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();

        return await _db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new CustomerDto(x.Id, x.Name, x.Phone, x.Email, x.Area, x.Balance, x.IsActive))
            .ToListAsync(ct);
    }

    [HttpGet("{id:guid}/activity")]
    public async Task<ActionResult<IReadOnlyList<CustomerActivityRowDto>>> Activity(Guid id, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();

        var customerExists = await _db.Customers.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive, ct);
        if (!customerExists) return NotFound();

        var sales = await _db.Sales.AsNoTracking()
            .Include(s => s.Payments)
            .Include(s => s.Lines).ThenInclude(l => l.Product)
            .Where(s => s.CustomerId == id && s.SoldAtUtc >= fromUtc && s.SoldAtUtc < toUtc)
            .ToListAsync(ct);

        var payments = await _db.CustomerPayments.AsNoTracking()
            .Where(p => p.CustomerId == id && p.PaidAtUtc >= fromUtc && p.PaidAtUtc < toUtc)
            .ToListAsync(ct);

        var rows = sales.Select(s => new CustomerActivityRowDto(
                s.SoldAtUtc,
                "Receipt",
                s.Payments.Count == 0 ? "Unknown" : string.Join(", ", s.Payments.Select(p => p.Method.ToString()).Distinct(StringComparer.OrdinalIgnoreCase)),
                s.Total,
                s.Id.ToString("N")[..8].ToUpperInvariant(),
                string.Join("; ", s.Lines.Select(l => $"{l.Product?.Name ?? "Unknown"} x {l.Qty:0.###}")),
                s.Id,
                s.Subtotal,
                s.Lines.Select(l => new SaleLogLineDto(l.Id, l.ProductId, l.Product?.Name ?? "Unknown", l.Qty, l.UnitPrice, l.LineTotal)).ToList()))
            .Concat(payments.Select(p => new CustomerActivityRowDto(
                p.PaidAtUtc,
                "Payment",
                p.Method,
                p.Amount,
                p.Note ?? string.Empty,
                string.IsNullOrWhiteSpace(p.ReferenceNo) ? string.Empty : $"Reference: {p.ReferenceNo}")))
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToList();

        return rows;
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(UpsertCustomerRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        var c = new Customer
        {
            Id = Guid.NewGuid(), Name = req.Name.Trim(), Phone = req.Phone.Trim(), Email = req.Email.Trim(), Area = req.Area.Trim(),
            Balance = req.Balance, IsActive = req.IsActive, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        _db.Customers.Add(c);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/customers/{c.Id}", new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Area, c.Balance, c.IsActive));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, UpsertCustomerRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        c.Name = req.Name.Trim(); c.Phone = req.Phone.Trim(); c.Email = req.Email.Trim(); c.Area = req.Area.Trim();
        c.Balance = req.Balance; c.IsActive = req.IsActive; c.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Area, c.Balance, c.IsActive);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        c.IsActive = false;
        c.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<CustomerDto>> ApplyPayment(Guid id, CustomerPaymentRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser, UserRole.Accountant, UserRole.Cashier)) return Forbid();
        if (req.Amount <= 0m) return BadRequest("Payment amount must be greater than zero.");

        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (c is null) return NotFound();

        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            CustomerId = c.Id,
            Amount = Math.Round(req.Amount, 2, MidpointRounding.AwayFromZero),
            Method = string.IsNullOrWhiteSpace(req.Method) ? "Payment" : req.Method.Trim(),
            ReferenceNo = string.IsNullOrWhiteSpace(req.ReferenceNo) ? null : req.ReferenceNo.Trim(),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            PaidAtUtc = DateTime.UtcNow
        };

        c.Balance = Math.Round(Math.Max(0m, c.Balance - payment.Amount), 2, MidpointRounding.AwayFromZero);
        c.UpdatedAtUtc = DateTime.UtcNow;
        _db.CustomerPayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return new CustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Area, c.Balance, c.IsActive);
    }
}
