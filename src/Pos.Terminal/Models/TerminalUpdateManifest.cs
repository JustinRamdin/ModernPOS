namespace Pos.Terminal.Models;

public sealed class TerminalUpdateManifest
{
    public string App { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string MinServerVersion { get; set; } = string.Empty;
    public string MaxServerVersion { get; set; } = string.Empty;
    public bool Mandatory { get; set; }
    public string InstallerFile { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
