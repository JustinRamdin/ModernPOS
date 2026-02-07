using System.IO;
using System;
using Microsoft.EntityFrameworkCore;
using DataLocalDb = Pos.Local.Data.LocalDb;

namespace Pos.Local.Data;

public static class LocalDb
{
public static string DefaultDbPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(root, "ModernPOS");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "pos.local.db");
        }
    }   

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
