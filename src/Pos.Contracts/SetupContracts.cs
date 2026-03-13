namespace Pos.Contracts;

public sealed record BootstrapServerRequest(string CompanyName, string SuperUsername, string SuperPassword, int ServerPort);
public sealed record CreateUserApiRequest(string Username, string Password, string Role, string? DisplayName = null);

public sealed record BackupRequest(string? BackupFolder = null);
public sealed record RestoreBackupRequest(string BackupFilePath);
public sealed record ScheduledBackupSettings(bool Enabled, string BackupFolder, TimeOnly DailyTimeUtc, int RetentionCount);

public sealed record ServerDashboardDto(
    string CompanyName,
    int Port,
    string DatabasePath,
    DateTimeOffset? LastBackupAtUtc,
    ScheduledBackupSettings Schedule,
    bool Initialized);
