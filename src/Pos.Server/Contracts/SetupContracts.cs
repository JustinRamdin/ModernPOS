namespace Pos.Server.Contracts;

public sealed record BootstrapServerRequest(
    string CompanyName,
    string SuperUsername,
    string SuperPassword,
    int ServerPort
);

public sealed record CreateUserApiRequest(
    string Username,
    string Password,
    string Role
);