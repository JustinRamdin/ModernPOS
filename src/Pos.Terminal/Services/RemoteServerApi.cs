using System.Net.Http.Json;
using Pos.Application.Auth;
using Pos.Server.Contracts;

namespace Pos.Terminal.Services;

public sealed class RemoteServerApi : IDisposable
{
    private readonly HttpClient _http;

    public RemoteServerApi(string host, int port)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}:{port}/")
        };
    }

    public async Task BootstrapAsync(string companyName, string superUsername, string superPassword, int serverPort)
    {
        var response = await _http.PostAsJsonAsync(
            "api/setup/bootstrap",
            new BootstrapServerRequest(companyName, superUsername, superPassword, serverPort));

        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Bootstrap failed: {(int)response.StatusCode} {body}");
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode} {body}");
        }

        var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>();
        if (loginResult is null)
            throw new InvalidOperationException("Login returned an empty response.");

        return loginResult;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
