using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;
using DataLocalDb = Pos.Local.Data.LocalDb;
namespace Pos.Local.Services;

public static class LocalDb
{
    public static string DefaultDbPath => DataLocalDb.DefaultDbPath;

    public static DbContextOptions<PosLocalDbContext> BuildOptions(string? dbPath = null)
        => DataLocalDb.BuildOptions(dbPath);

    /// <summary>
    /// Create schema directly from model (no migrations). Best for rapid iteration.
    /// </summary>
    public static async Task EnsureCreatedAsync(string? dbPath = null, CancellationToken ct = default)
    {
        var options = BuildOptions(dbPath);
        await using var db = new PosLocalDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
    }

    // ✅ OVERLOAD: no-argument migrate for existing call sites (like App.axaml.cs)
    public static Task MigrateAsync(CancellationToken ct = default)
        => EnsureCreatedAsync(DefaultDbPath, ct);

    // ✅ OVERLOAD: dbPath migrate for callers that pass a path
    public static Task MigrateAsync(string dbPath, CancellationToken ct = default)
        => EnsureCreatedAsync(dbPath, ct);
}
