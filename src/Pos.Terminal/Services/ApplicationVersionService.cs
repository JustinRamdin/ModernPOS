using System.Reflection;

namespace Pos.Terminal.Services;

public static class ApplicationVersionService
{
    public static string GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? typeof(ApplicationVersionService).Assembly.GetName().Version;

        if (version is null)
            return "0.0.0";

        return version.Build >= 0
            ? version.ToString(3)
            : $"{version.Major}.{version.Minor}.0";
    }
}
