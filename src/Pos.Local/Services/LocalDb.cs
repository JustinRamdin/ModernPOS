using Microsoft.EntityFrameworkCore;
using Pos.Local.Data;

namespace Pos.Local.Services;

public static class LocalDb
{
    public static string DefaultDbPath => "pos.local.db";

    public static DbContextOptions<PosLocalDbContext> BuildOptions(string? dbPath = null)
    {
        dbPath ??= DefaultDbPath;

        var builder = new DbContextOptionsBuilder<PosLocalDbContext>();
        builder.UseSqlite($"Data Source={dbPath};Foreign Keys=True;");
        return builder.Options;
    }

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
