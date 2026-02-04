using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pos.Local.Data;

namespace Pos.Local.Data;

public class PosLocalDbContextFactory : IDesignTimeDbContextFactory<PosLocalDbContext>
{
    public PosLocalDbContext CreateDbContext(string[] args)
    {
        string? dbPath = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--db", StringComparison.OrdinalIgnoreCase))
                dbPath = args[i + 1];
        }

        var options = LocalDb.BuildOptions(dbPath);
        return new PosLocalDbContext(options);
    }
}
