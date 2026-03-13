using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Auth;
using Pos.Infrastructure.Data;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(PosDbContext db, IPasswordHasher hasher, ISessionTokenStore sessions) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResult>> Login([FromBody] LoginRequest request)
    {
        var user = await db.UserAccounts.Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Username == request.Username && x.IsActive);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash) || user.Company is null)
        {
            return Unauthorized();
        }

        var principal = new SessionPrincipal(user.Id, user.CompanyId, user.Username, user.Role);
        var token = sessions.Issue(principal);
        return Ok(new LoginResult(token, user.Company.Name, user.Role, user.Username));
    }
}
