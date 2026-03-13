using Pos.Domain.Entities;

namespace Pos.Application.Auth;

public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResult(string Token, string CompanyName, UserRole Role, string Username);
