using System.Text.Json;
using Pos.Contracts;

namespace Pos.Server.Services;

public sealed class ScheduledBackupOptionsStore
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "scheduled-backup.json");

    public ScheduledBackupSettings Load()
        => File.Exists(FilePath)
            ? JsonSerializer.Deserialize<ScheduledBackupSettings>(File.ReadAllText(FilePath)) ?? Defaults()
            : Defaults();

    public void Save(ScheduledBackupSettings settings)
        => File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    private static ScheduledBackupSettings Defaults() => new(false, Path.Combine(AppContext.BaseDirectory, "backups"), new TimeOnly(2, 0), 14);
}
