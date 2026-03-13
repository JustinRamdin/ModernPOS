using System.Collections.Concurrent;
using Pos.Application.Auth;

namespace Pos.Infrastructure.Auth;

public sealed class InMemorySessionTokenStore : ISessionTokenStore
{
    private readonly ConcurrentDictionary<string, SessionPrincipal> _sessions = new();

    public string Issue(SessionPrincipal principal)
    {
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        _sessions[token] = principal;
        return token;
    }

    public bool TryGet(string token, out SessionPrincipal principal)
        => _sessions.TryGetValue(token, out principal!);
}
