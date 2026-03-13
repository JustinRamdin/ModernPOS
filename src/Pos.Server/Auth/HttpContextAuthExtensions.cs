using Pos.Application.Auth;
using Pos.Domain.Entities;

namespace Pos.Server.Auth;

public static class HttpContextAuthExtensions
{
    public static SessionPrincipal? CurrentPrincipal(this HttpContext http)
        => http.Items.TryGetValue(nameof(SessionPrincipal), out var value) ? value as SessionPrincipal : null;

    public static bool RequireRole(this HttpContext http, params UserRole[] roles)
    {
        var principal = http.CurrentPrincipal();
        return principal is not null && roles.Contains(principal.Role);
    }
}
