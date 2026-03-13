using Pos.Application.Auth;

namespace Pos.Server.Auth;

public sealed class SessionAuthMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context, ISessionTokenStore tokenStore)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = auth[7..].Trim();
            if (tokenStore.TryGet(token, out var principal))
            {
                context.Items[nameof(SessionPrincipal)] = principal;
            }
        }

        await next(context);
    }
}
