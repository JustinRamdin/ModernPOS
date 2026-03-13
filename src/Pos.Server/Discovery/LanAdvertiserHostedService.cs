using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Pos.Server.Discovery;

public sealed class LanAdvertiserHostedService(LanAdvertiserOptions options, ILogger<LanAdvertiserHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    service = "ModernPOS",
                    companyName = options.CompanyName,
                    port = options.ServerPort,
                    utc = DateTime.UtcNow
                });

                var bytes = Encoding.UTF8.GetBytes(payload);
                await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, options.UdpPort));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast LAN announcement.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
