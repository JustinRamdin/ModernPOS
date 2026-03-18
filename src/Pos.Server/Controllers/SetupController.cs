using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Auth;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Contracts;
using Pos.Server.Hosting;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/setup")]
public sealed class SetupController(PosDbContext db, IPasswordHasher hasher, ILogger<SetupController> logger) : ControllerBase
{
    [HttpPost("bootstrap")]
    public async Task<ActionResult<object>> Bootstrap([FromBody] BootstrapServerRequest request)
    {
        if (await db.Companies.AnyAsync())
        {
            return Conflict("Server is already initialized.");
        }

        var companyName = request.CompanyName.Trim();
        var company = new Company
        {
            Name = companyName,
            ReceiptHeaderTitle = companyName
        };
        var user = new UserAccount
        {
            Company = company,
            Username = request.SuperUsername.Trim(),
            PasswordHash = hasher.Hash(request.SuperPassword),
            DisplayName = request.SuperUsername.Trim(),
            Role = UserRole.SuperUser,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.Companies.Add(company);
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        await FirewallRuleService.TryAddWindowsRuleAsync(request.ServerPort, logger);

        return Ok(new { companyId = company.Id, superUserId = user.Id });
    }

    [HttpGet("status")]
    public async Task<ActionResult<object>> Status()
        => Ok(new { initialized = await db.Companies.AnyAsync() });
}
