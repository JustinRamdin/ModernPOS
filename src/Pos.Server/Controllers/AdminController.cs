using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Pos.Contracts;
using Pos.Infrastructure.Data;
using Pos.Server.Services;

namespace Pos.Server.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(
    PosDbContext db,
    BackupOrchestrator backups,
    ScheduledBackupOptionsStore scheduleStore,
     ServerRuntimeState runtime) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<ServerDashboardDto>> Dashboard()
    {
        var company = await db.Companies.Select(x => x.Name).FirstOrDefaultAsync() ?? "Unconfigured";
        var schedule = scheduleStore.Load();
        var conn = db.Database.GetConnectionString() ?? "Data Source=modernpos.server.db";
        var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(conn).DataSource;
        var port = HttpContext.Connection.LocalPort;
        return Ok(new ServerDashboardDto(company, port, Path.GetFullPath(path), runtime.LastBackupAtUtc, schedule, await db.Companies.AnyAsync()));
    }

    [HttpPost("backup")]
    public async Task<ActionResult<object>> Backup([FromBody] BackupRequest request)
    {
        var path = await backups.CreateBackupAsync(request.BackupFolder);
        return Ok(new { path });
    }

    [HttpPost("restore")]
    public async Task<ActionResult> Restore([FromBody] RestoreBackupRequest request)
    {
        await backups.RestoreAsync(request.BackupFilePath);
        return Ok();
    }

    [HttpPost("schedule")]
    public ActionResult SaveSchedule([FromBody] ScheduledBackupSettings settings)
    {
        scheduleStore.Save(settings);
        return Ok();
    }
      [HttpGet("version")]
    public ActionResult<ServerVersionInfoDto> Version()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? typeof(AdminController).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        var conn = db.Database.GetConnectionString() ?? $"Data Source={ServerStoragePaths.DefaultDatabasePath}";
        var path = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(conn).DataSource;
        var fullPath = Path.GetFullPath(path);

        return Ok(new ServerVersionInfoDto(
            version,
            fullPath,
            ServerStoragePaths.IsProtectedDataPath(fullPath),
            ManualServerUpdatesRequired: true));
    }
}
