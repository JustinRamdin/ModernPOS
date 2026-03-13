using Pos.Domain.Entities;

namespace Pos.Application.Users;

public sealed record CreateUserRequest(string Username, string Password, UserRole Role);
public sealed record UserSummary(Guid Id, string Username, UserRole Role, bool IsActive);
