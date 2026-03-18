namespace Pos.Contracts;

public sealed record BootstrapServerRequest(string CompanyName, string SuperUsername, string SuperPassword, int ServerPort);
public sealed record CreateUserApiRequest(string Username, string Password, string Role, string? DisplayName = null);
public sealed record UpdateUserApiRequest(string DisplayName, string Role, bool IsActive);
public sealed record ResetPasswordApiRequest(string NewPassword);
public sealed record UserSummaryDto(Guid Id, string Username, string DisplayName, string Role, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record BackupRequest(string? BackupFolder = null);
public sealed record RestoreBackupRequest(string BackupFilePath);
public sealed record BackupResponse(string Path);
public sealed record ScheduledBackupSettings(bool Enabled, string BackupFolder, TimeOnly DailyTimeUtc, int RetentionCount);

public sealed record ServerDashboardDto(
    string CompanyName,
    int Port,
    string DatabasePath,
    DateTimeOffset? LastBackupAtUtc,
    ScheduledBackupSettings Schedule,
    bool Initialized);

public sealed record ServerVersionInfoDto(
    string Version,
    string DatabasePath,
    bool DatabasePathIsProtected,
    bool ManualServerUpdatesRequired);