using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Pos.Contracts;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly PosDbContext _db;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(PosDbContext db, ILogger<ProductsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<InventoryItemDto>>> Get(CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Cashier, UserRole.Manager, UserRole.Accountant, UserRole.SuperUser)) return Unauthorized();
         var items = await _db.Products.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new InventoryItemDto(x.Id, x.Sku, x.Name, x.Description, x.CostPrice, x.Price, x.VatInclusive, x.IsLength, x.OnHand, x.OnHandInches, x.IsActive))
            .ToListAsync(ct);

        return items;
    }

    [HttpPost]
    public async Task<ActionResult<InventoryItemDto>> Create(UpsertInventoryItemRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();

        var p = new Product
        {
            Id = Guid.NewGuid(),
            Sku = req.Sku.Trim(),
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            CostPrice = req.CostPrice,
            Price = req.Price,
            VatInclusive = req.VatInclusive,
            IsLength = req.IsLength,
            OnHand = req.IsLength ? 0 : req.OnHand,
            OnHandInches = req.IsLength ? req.OnHandInches : 0,
            IsActive = req.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Products.Add(p);

          try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create product {Sku}.", p.Sku);
            var dbError = ex.InnerException is DbException dbEx ? dbEx.Message : ex.Message;
            return Problem(title: "Failed to save inventory item", detail: dbError, statusCode: StatusCodes.Status500InternalServerError);
        }
        
        return Created($"/api/products/{p.Id}", new InventoryItemDto(p.Id, p.Sku, p.Name, p.Description, p.CostPrice, p.Price, p.VatInclusive, p.IsLength, p.OnHand, p.OnHandInches, p.IsActive));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryItemDto>> Update(Guid id, UpsertInventoryItemRequest req, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        p.Sku = req.Sku.Trim();
        p.Name = req.Name.Trim();
        p.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        p.CostPrice = req.CostPrice;
        p.Price = req.Price;
        p.VatInclusive = req.VatInclusive;
        p.IsLength = req.IsLength;
        p.OnHand = req.IsLength ? 0 : req.OnHand;
        p.OnHandInches = req.IsLength ? req.OnHandInches : 0;
        p.IsActive = req.IsActive;

        await _db.SaveChangesAsync(ct);
        return new InventoryItemDto(p.Id, p.Sku, p.Name, p.Description, p.CostPrice, p.Price, p.VatInclusive, p.IsLength, p.OnHand, p.OnHandInches, p.IsActive);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!HttpContext.RequireRole(UserRole.Manager, UserRole.SuperUser)) return Forbid();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        p.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
