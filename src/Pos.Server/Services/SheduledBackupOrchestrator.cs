using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pos.Server.Services;

public sealed class ScheduledBackupHostedService(IServiceScopeFactory scopeFactory, ScheduledBackupOptionsStore store, ILogger<ScheduledBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var schedule = store.Load();
                var now = DateTime.UtcNow;
                var next = now.Date.Add(schedule.DailyTimeUtc.ToTimeSpan());
                if (next <= now) next = next.AddDays(1);
                await Task.Delay(next - now, stoppingToken);

                schedule = store.Load();
                if (!schedule.Enabled) continue;

                using var scope = scopeFactory.CreateScope();
                var backups = scope.ServiceProvider.GetRequiredService<BackupOrchestrator>();
                var file = await backups.CreateBackupAsync(schedule.BackupFolder, stoppingToken);
                ApplyRetention(schedule.BackupFolder, schedule.RetentionCount);
                logger.LogInformation("Scheduled backup created: {File}", file);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "Scheduled backup failed"); }
        }
    }

    private static void ApplyRetention(string folder, int retention)
    {
        if (retention <= 0 || !Directory.Exists(folder)) return;
        var files = new DirectoryInfo(folder).GetFiles("modernpos-backup-*.db").OrderByDescending(x => x.CreationTimeUtc).ToList();
        foreach (var f in files.Skip(retention)) f.Delete();
    }
}
