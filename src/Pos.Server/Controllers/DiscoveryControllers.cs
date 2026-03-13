using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Data;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/discovery")]
public sealed class DiscoveryController(PosDbContext db) : ControllerBase
{
    [HttpGet("info")]
    public async Task<ActionResult<object>> GetInfo()
    {
        var company = await db.Companies.Select(x => x.Name).FirstOrDefaultAsync();
        return Ok(new
        {
            service = "ModernPOS",
            companyName = company ?? "Unconfigured",
            port = HttpContext.Connection.LocalPort
        });
    }
}
