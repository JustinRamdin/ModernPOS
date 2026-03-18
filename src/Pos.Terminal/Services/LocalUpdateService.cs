using System.Text.Json;
using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public sealed class LocalUpdateService
{
    public const string ManifestFileName = "terminal-manifest.json";

    public async Task<TerminalUpdateManifest> LoadManifestAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new InvalidOperationException("Select a local update folder first.");

        var fullFolderPath = Path.GetFullPath(folderPath.Trim());
        if (!Directory.Exists(fullFolderPath))
            throw new DirectoryNotFoundException($"Update folder not found: {fullFolderPath}");

        var manifestPath = Path.Combine(fullFolderPath, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest file '{ManifestFileName}' was not found in the selected folder.", manifestPath);

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<TerminalUpdateManifest>(stream, cancellationToken: ct)
            ?? throw new InvalidOperationException("Update manifest is empty or invalid JSON.");

        ValidateManifest(manifest);
        return manifest;
    }

    public string ResolveInstallerPath(string folderPath, TerminalUpdateManifest manifest)
    {
        var fullFolderPath = Path.GetFullPath(folderPath);
        var installerPath = Path.GetFullPath(Path.Combine(fullFolderPath, manifest.InstallerFile));
        var folderPrefix = fullFolderPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullFolderPath
            : fullFolderPath + Path.DirectorySeparatorChar;

        if (!installerPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Installer file must remain inside the selected update folder.");

        if (!File.Exists(installerPath))
            throw new FileNotFoundException($"Installer package '{manifest.InstallerFile}' was not found.", installerPath);

        var extension = Path.GetExtension(installerPath);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .exe and .msi client installer packages are supported.");
        }

        return installerPath;
    }

    private static void ValidateManifest(TerminalUpdateManifest manifest)
    {
        if (!string.Equals(manifest.App, "terminal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only terminal update manifests are supported by this screen.");

        if (!VersionComparer.TryParse(manifest.Version, out _))
            throw new InvalidOperationException("Manifest version is missing or invalid.");

        if (!VersionComparer.TryParse(manifest.MinServerVersion, out _))
            throw new InvalidOperationException("Manifest minServerVersion is missing or invalid.");

        if (!VersionComparer.TryParse(manifest.MaxServerVersion, out _))
            throw new InvalidOperationException("Manifest maxServerVersion is missing or invalid.");

        if (string.IsNullOrWhiteSpace(manifest.InstallerFile))
            throw new InvalidOperationException("Manifest installerFile is required.");

        if (string.IsNullOrWhiteSpace(manifest.Notes))
            throw new InvalidOperationException("Manifest notes are required.");

        if (string.IsNullOrWhiteSpace(manifest.Type))
            throw new InvalidOperationException("Manifest type is required.");
    }
}
