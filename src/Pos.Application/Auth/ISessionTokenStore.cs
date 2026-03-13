using Pos.Domain.Entities;

namespace Pos.Application.Auth;

public sealed record SessionPrincipal(Guid UserId, Guid CompanyId, string Username, UserRole Role);

public interface ISessionTokenStore
{
    string Issue(SessionPrincipal principal);
    bool TryGet(string token, out SessionPrincipal principal);
}
