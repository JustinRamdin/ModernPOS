using System.Diagnostics;
using RuntimeOS = System.Runtime.InteropServices.RuntimeInformation;
using OSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace Pos.Server.Hosting;

public static class FirewallRuleService
{
    public static async Task TryAddWindowsRuleAsync(int port, ILogger logger)
    {
        if (!RuntimeOS.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var cmd = $"advfirewall firewall add rule name=\"ModernPOS {port}\" dir=in action=allow protocol=TCP localport={port}";
        var startInfo = new ProcessStartInfo("netsh", cmd)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Firewall rule creation returned {Code}: {Error}", process.ExitCode, await process.StandardError.ReadToEndAsync());
        }
    }
}
