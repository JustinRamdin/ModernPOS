using System.Net.Http.Json;
using Pos.Contracts;

namespace Pos.ServerApp.Services;

public sealed class ServerAdminApi
{
    private readonly HttpClient _http;

    public ServerAdminApi(string host, int port)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}/") };
    }

    public async Task BootstrapAsync(BootstrapServerRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/setup/bootstrap", request);
        response.EnsureSuccessStatusCode();
    }

    public Task<ServerDashboardDto?> GetDashboardAsync() => _http.GetFromJsonAsync<ServerDashboardDto>("api/admin/dashboard");
    public async Task TriggerBackupAsync(string? folder = null) => (await _http.PostAsJsonAsync("api/admin/backup", new BackupRequest(folder))).EnsureSuccessStatusCode();
    public async Task RestoreAsync(string file) => (await _http.PostAsJsonAsync("api/admin/restore", new RestoreBackupRequest(file))).EnsureSuccessStatusCode();
    public async Task SaveScheduleAsync(ScheduledBackupSettings s) => (await _http.PostAsJsonAsync("api/admin/schedule", s)).EnsureSuccessStatusCode();
}
