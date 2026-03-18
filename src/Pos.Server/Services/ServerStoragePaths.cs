namespace Pos.Server.Services;

public static class ServerStoragePaths
{
    public static string DataRoot
    {
        get
        {
            var baseFolder = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var folder = Path.Combine(baseFolder, "ModernPOS", "server");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string DefaultDatabasePath => Path.Combine(DataRoot, "modernpos.db");
    public static string DefaultBackupFolder => Path.Combine(DataRoot, "backups");
    public static string ScheduledBackupSettingsPath => Path.Combine(DataRoot, "scheduled-backup.json");

    public static bool IsProtectedDataPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var installRoot = Path.GetFullPath(AppContext.BaseDirectory);

        return !fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase);
    }
}
