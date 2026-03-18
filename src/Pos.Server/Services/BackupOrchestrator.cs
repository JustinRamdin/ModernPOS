using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pos.Infrastructure.Data;

namespace Pos.Server.Services;

public sealed class BackupOrchestrator(PosDbContext db, ILogger<BackupOrchestrator> logger, ServerRuntimeState runtimeState)
{
    private readonly SemaphoreSlim _gate = new(1,1);

    public async Task<string> CreateBackupAsync(string? folder, CancellationToken ct = default)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? ServerStoragePaths.DefaultBackupFolder : folder;
        Directory.CreateDirectory(folder);

        var dbPath = ResolveDbPath();
        var file = Path.Combine(folder, $"modernpos-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        await _gate.WaitAsync(ct);
        try
        {
            await db.Database.CloseConnectionAsync();
            File.Copy(dbPath, file, overwrite: false);
            runtimeState.LastBackupAtUtc = DateTimeOffset.UtcNow;
            logger.LogInformation("Backup created at {File}", file);
            return file;
        }
        finally { _gate.Release(); }
    }

    public async Task RestoreAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(backupFilePath)) throw new FileNotFoundException("Backup file not found", backupFilePath);
        var dbPath = ResolveDbPath();

        await _gate.WaitAsync(ct);
        try
        {
            await db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();
            File.Copy(backupFilePath, dbPath, overwrite: true);
            logger.LogWarning("Database restored from {File}", backupFilePath);
        }
        finally { _gate.Release(); }
    }

    private string ResolveDbPath()
    {
        var conn = db.Database.GetConnectionString() ?? throw new InvalidOperationException("Missing connection string.");
        var builder = new SqliteConnectionStringBuilder(conn);
        return Path.GetFullPath(builder.DataSource);
    }
}
