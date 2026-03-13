namespace Pos.Server.Services;

public sealed class ServerRuntimeState
{
    public DateTimeOffset? LastBackupAtUtc { get; set; }
}
