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
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> Get()
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        var users = await db.UserAccounts
            .Where(x => x.CompanyId == principal.CompanyId)
             .OrderBy(x => x.Username)
            .Select(x => new UserSummaryDto(x.Id, x.Username, x.DisplayName, x.Role.ToString(), x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserSummaryDto>> Create([FromBody] CreateUserApiRequest request)
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest("Unknown role.");

        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("Username is required.");

        var exists = await db.UserAccounts.AnyAsync(x => x.CompanyId == principal.CompanyId && x.Username == username);
        if (exists)
            return Conflict("Username already exists.");

        var user = new UserAccount
        {
            CompanyId = principal.CompanyId,
            Username = username,
            PasswordHash = hasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim(),
            Role = role,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        return Ok(new UserSummaryDto(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.IsActive, user.CreatedAtUtc, user.UpdatedAtUtc));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserSummaryDto>> Update(Guid id, [FromBody] UpdateUserApiRequest request)
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest("Unknown role.");

        var user = await db.UserAccounts.FirstOrDefaultAsync(x => x.CompanyId == principal.CompanyId && x.Id == id);
        if (user is null)
            return NotFound();

        if (user.Id == principal.UserId && !request.IsActive)
            return BadRequest("Super user cannot deactivate the active session account.");

        user.DisplayName = request.DisplayName?.Trim() ?? user.Username;
        user.Role = role;
        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new UserSummaryDto(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.IsActive, user.CreatedAtUtc, user.UpdatedAtUtc));
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordApiRequest request)
    {
        var principal = HttpContext.CurrentPrincipal();
        if (principal is null)
            return Unauthorized();

        if (!HttpContext.RequireRole(UserRole.SuperUser))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 4)
            return BadRequest("Password must be at least 4 characters.");

        var user = await db.UserAccounts.FirstOrDefaultAsync(x => x.CompanyId == principal.CompanyId && x.Id == id);
        if (user is null)
            return NotFound();

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok();
    }
}
