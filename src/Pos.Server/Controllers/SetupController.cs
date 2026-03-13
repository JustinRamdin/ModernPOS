using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Auth;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Contracts;
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

        var company = new Company { Name = request.CompanyName.Trim() };
        var user = new UserAccount
        {
            Company = company,
            Username = request.SuperUsername.Trim(),
            PasswordHash = hasher.Hash(request.SuperPassword),
            Role = UserRole.SuperUser
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
