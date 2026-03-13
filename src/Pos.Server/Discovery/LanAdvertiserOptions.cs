namespace Pos.Server.Discovery;

public sealed class LanAdvertiserOptions
{
    public int UdpPort { get; set; } = 40444;
    public int ServerPort { get; set; } = 5050;
    public string CompanyName { get; set; } = "Unconfigured";
}
