using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Auth;
using Pos.Domain.Entities;
using Pos.Infrastructure.Data;
using Pos.Server.Auth;
using Pos.Contracts;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(PosDbContext db, IPasswordHasher hasher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get()
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        var users = await db.UserAccounts
            .Where(x => x.CompanyId == principal.CompanyId)
            .Select(x => new { x.Id, x.Username, x.DisplayName, role = x.Role.ToString(), x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateUserApiRequest request)
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest("Unknown role.");

        var exists = await db.UserAccounts.AnyAsync(x => x.CompanyId == principal.CompanyId && x.Username == request.Username);
        if (exists)
            return Conflict("Username already exists.");

        var user = new UserAccount
        {
            CompanyId = principal.CompanyId,
            Username = request.Username.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            DisplayName = request.DisplayName?.Trim() ?? request.Username.Trim(),
            Role = role,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        return Ok(new { user.Id, user.Username, role = user.Role.ToString() });
    }
}
