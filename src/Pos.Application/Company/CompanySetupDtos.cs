namespace Pos.Application.Company;

public sealed record CompanySetupRequest(string CompanyName, string SuperUsername, string SuperPassword);
public sealed record CompanySetupResult(Guid CompanyId, Guid SuperUserId);
