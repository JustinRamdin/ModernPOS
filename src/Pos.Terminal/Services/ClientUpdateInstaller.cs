using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Pos.Terminal.Services;

public sealed class ClientUpdateInstaller
{
    public Task LaunchInstallerAsync(string installerPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
            throw new InvalidOperationException("Installer path is required.");

        var fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Installer package was not found.", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (OperatingSystem.IsWindows())
        {
            LaunchWindowsInstaller(fullPath, extension);
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)
            });
        }

         if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();

        return Task.CompletedTask;
    }

    private static void LaunchWindowsInstaller(string fullPath, string extension)
    {
        if (string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec",
                Arguments = $"/i \"{fullPath}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)
            });
            return;
        }

        var launcherPath = Path.Combine(Path.GetTempPath(), $"modernpos-terminal-update-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(launcherPath, $"""
            @echo off
            timeout /t 2 /nobreak >nul
            start "" "{fullPath}"
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{launcherPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(fullPath)
        });
    }
}
