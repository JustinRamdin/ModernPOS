using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    ServerRuntimeState runtime,
    IConfiguration config) : ControllerBase
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
}
