using System.Net.Http.Json;
using System.Net.Http.Headers;
using Pos.Application.Auth;
using Pos.Contracts;

namespace Pos.Terminal.Services;

public sealed class RemoteServerApi : IDisposable
{
    private readonly HttpClient _http;

    public RemoteServerApi(string host, int port, string? authToken = null)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}/") };
        if (!string.IsNullOrWhiteSpace(authToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>() ?? throw new InvalidOperationException("Empty login response");
    }

         public async Task<IReadOnlyList<object>> GetUsersAsync()
        => await _http.GetFromJsonAsync<List<object>>("api/users") ?? [];

        public async Task CreateUserAsync(CreateUserApiRequest request)
        => (await _http.PostAsJsonAsync("api/users", request)).EnsureSuccessStatusCode();

        public async Task TriggerBackupAsync()
        => (await _http.PostAsJsonAsync("api/admin/backup", new BackupRequest())).EnsureSuccessStatusCode();

    public async Task BootstrapAsync(BootstrapServerRequest request)
        => (await _http.PostAsJsonAsync("api/setup/bootstrap", request)).EnsureSuccessStatusCode();

    public void Dispose() => _http.Dispose();
}
