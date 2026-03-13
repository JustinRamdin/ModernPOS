using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly PosDbContext _db;
    public ProductsController(PosDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<Product>>> Get()
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();
        return await _db.Products.OrderBy(x => x.Name).ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(Product p)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        p.Id = Guid.NewGuid();
        p.CreatedAtUtc = DateTime.UtcNow;
        _db.Products.Add(p);
        await _db.SaveChangesAsync();
        return Created($"/api/products/{p.Id}", p);
    }
}
