using System.Text.Json;

namespace Pos.ServerApp.Services;

public sealed class ServerAppSettings
{
    public bool IsConfigured { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int Port { get; set; } = 5050;
    public string ConnectionString { get; set; } = "Data Source=modernpos.server.db";
}

public sealed class ServerAppSettingsStore
{
    private static readonly string PathValue = System.IO.Path.Combine(AppContext.BaseDirectory, "serverapp.settings.json");

    public ServerAppSettings Load()
        => File.Exists(PathValue)
            ? JsonSerializer.Deserialize<ServerAppSettings>(File.ReadAllText(PathValue)) ?? new ServerAppSettings()
            : new ServerAppSettings();

    public void Save(ServerAppSettings settings)
        => File.WriteAllText(PathValue, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
}
