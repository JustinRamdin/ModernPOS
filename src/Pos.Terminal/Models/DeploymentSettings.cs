namespace Pos.Terminal.Models;

public sealed class DeploymentSettings
{
    public string Mode { get; set; } = "Client";
    public string ServerHost { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 5050;
    public string CompanyName { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
}
