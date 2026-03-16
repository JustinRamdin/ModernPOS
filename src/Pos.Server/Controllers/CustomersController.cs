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
}
