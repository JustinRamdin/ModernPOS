using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Pos.Local.Data;

public static class LocalDb
{
    public static string DefaultDbPath =>
        Path.Combine(@"C:\ModernPOS\Data", "poslocal.db");

    public static DbContextOptions<PosLocalDbContext> BuildOptions(string? dbPath = null)
    {
        var finalPath = string.IsNullOrWhiteSpace(dbPath) ? DefaultDbPath : dbPath;

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        return new DbContextOptionsBuilder<PosLocalDbContext>()
            .UseSqlite($"Data Source={finalPath}")
            .Options;
    }

    public static PosLocalDbContext Create(string? dbPath = null)
        => new PosLocalDbContext(BuildOptions(dbPath));
}
