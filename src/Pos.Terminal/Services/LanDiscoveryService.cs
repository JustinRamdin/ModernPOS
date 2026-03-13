using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public sealed class LanDiscoveryService
{
    public async Task<IReadOnlyList<DiscoveredServer>> ScanAsync(int udpPort = 40444, int timeoutMs = 2500)
    {
        var found = new Dictionary<string, DiscoveredServer>(StringComparer.OrdinalIgnoreCase);
        using var udp = new UdpClient(udpPort);
        using var cts = new CancellationTokenSource(timeoutMs);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(cts.Token);
                using var doc = JsonDocument.Parse(result.Buffer);
                var root = doc.RootElement;
                if (!root.TryGetProperty("service", out var service) || service.GetString() != "ModernPOS")
                    continue;

                var company = root.GetProperty("companyName").GetString() ?? "Unknown";
                var port = root.GetProperty("port").GetInt32();
                var ip = result.RemoteEndPoint.Address.ToString();
                found[$"{ip}:{port}"] = new DiscoveredServer(company, ip, port);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // ignored per packet
            }
        }

        return found.Values.OrderBy(x => x.CompanyName).ToList();
    }
}
