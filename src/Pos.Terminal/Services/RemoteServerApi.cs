using System.Net.Http.Json;
using System.Net.Http.Headers;
using Pos.Application.Auth;
using Pos.Contracts;
using Pos.Terminal.Models;

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

    public async Task ValidateServerAsync()
    {
        var response = await _http.GetAsync("health");
        response.EnsureSuccessStatusCode();
    }
    
    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>() ?? throw new InvalidOperationException("Empty login response");
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync()
        => await _http.GetFromJsonAsync<List<ProductDto>>("api/products") ?? [];
    
    public sealed record ServerCheckoutResponse(Guid SaleId, decimal Total, decimal Paid, decimal Change);

    public async Task<ServerCheckoutResponse> CheckoutAsync(CheckoutRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/sales/checkout", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServerCheckoutResponse>()
            ?? throw new InvalidOperationException("Empty checkout response");
    }
         public async Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync()
        => await _http.GetFromJsonAsync<List<UserSummaryDto>>("api/users") ?? [];

    public async Task<UserSummaryDto> CreateUserAsync(CreateUserApiRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/users", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserSummaryDto>() ?? throw new InvalidOperationException("Empty create user response");
    }

    public async Task<UserSummaryDto> UpdateUserAsync(Guid userId, UpdateUserApiRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/users/{userId}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserSummaryDto>() ?? throw new InvalidOperationException("Empty update user response");
    }

    public async Task ResetUserPasswordAsync(Guid userId, string newPassword)
        => (await _http.PostAsJsonAsync($"api/users/{userId}/reset-password", new ResetPasswordApiRequest(newPassword))).EnsureSuccessStatusCode();

    public async Task<BackupResponse> TriggerBackupAsync(string? backupFolder = null)
    {
        var response = await _http.PostAsJsonAsync("api/admin/backup", new BackupRequest(backupFolder));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BackupResponse>() ?? throw new InvalidOperationException("Empty backup response");
    }

       public async Task<ServerDashboardDto> GetDashboardAsync()
        => await _http.GetFromJsonAsync<ServerDashboardDto>("api/admin/dashboard") ?? throw new InvalidOperationException("Empty dashboard response");
    public async Task BootstrapAsync(BootstrapServerRequest request)
        => (await _http.PostAsJsonAsync("api/setup/bootstrap", request)).EnsureSuccessStatusCode();

    public async Task<IReadOnlyList<InventoryItemDto>> GetInventoryAsync()
        => await _http.GetFromJsonAsync<List<InventoryItemDto>>("api/products") ?? [];

    public async Task<InventoryItemDto> CreateInventoryAsync(UpsertInventoryItemRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/products", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryItemDto>() ?? throw new InvalidOperationException("Empty inventory create response");
    }

    public async Task<InventoryItemDto> UpdateInventoryAsync(Guid id, UpsertInventoryItemRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/products/{id}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryItemDto>() ?? throw new InvalidOperationException("Empty inventory update response");
    }

    public async Task DeleteInventoryAsync(Guid id)
        => (await _http.DeleteAsync($"api/products/{id}")).EnsureSuccessStatusCode();

    public async Task<IReadOnlyList<CustomerDto>> GetCustomersAsync()
        => await _http.GetFromJsonAsync<List<CustomerDto>>("api/customers") ?? [];

    public async Task<CustomerDto> CreateCustomerAsync(UpsertCustomerRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/customers", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerDto>() ?? throw new InvalidOperationException("Empty customer create response");
    }

    public async Task<CustomerDto> UpdateCustomerAsync(Guid id, UpsertCustomerRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/customers/{id}", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerDto>() ?? throw new InvalidOperationException("Empty customer update response");
    }

    public async Task DeleteCustomerAsync(Guid id)
        => (await _http.DeleteAsync($"api/customers/{id}")).EnsureSuccessStatusCode();

    public async Task<ReportSummaryDto> GetReportSummaryAsync(DateTime fromUtc, DateTime toUtc)
    {
        var url = $"api/reports/summary?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        return await _http.GetFromJsonAsync<ReportSummaryDto>(url) ?? throw new InvalidOperationException("Empty reports response");
    }

    public void Dispose() => _http.Dispose();
}
