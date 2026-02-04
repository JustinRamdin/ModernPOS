using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly PosDbContext _db;
    public ProductsController(PosDbContext db) => _db = db;

    [HttpGet]
    public async Task<List<Product>> Get() =>
        await _db.Products.OrderBy(x => x.Name).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Product>> Create(Product p)
    {
        p.Id = Guid.NewGuid();
        p.CreatedAtUtc = DateTime.UtcNow;
        _db.Products.Add(p);
        await _db.SaveChangesAsync();
        return Created($"/api/products/{p.Id}", p);
    }
}
