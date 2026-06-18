using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Pos.Application.Auth;
using Pos.Contracts;
using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public sealed class RemoteServerApi : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public async Task<CompanyProfileDto> GetCompanyProfileAsync()
        => await _http.GetFromJsonAsync<CompanyProfileDto>("api/company-profile") ?? throw new InvalidOperationException("Empty company profile response");
    public sealed record ServerCheckoutResponse(Guid SaleId, decimal Total, decimal Paid, decimal Change);

    public async Task<ServerCheckoutResponse> CheckoutAsync(Pos.Contracts.CheckoutRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/sales/checkout", request);
        if (!response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync()).Trim();
            var detail = string.IsNullOrWhiteSpace(body) ? "No response body." : body;
            throw new HttpRequestException(
                $"Checkout failed: {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
                null,
                response.StatusCode);
        }

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

    public async Task RestoreBackupAsync(string backupFilePath)
        => (await _http.PostAsJsonAsync("api/admin/restore", new RestoreBackupRequest(backupFilePath))).EnsureSuccessStatusCode();

    public async Task SaveBackupScheduleAsync(ScheduledBackupSettings settings)
        => (await _http.PostAsJsonAsync("api/admin/schedule", settings)).EnsureSuccessStatusCode();
    public async Task<ServerDashboardDto> GetDashboardAsync()
        => await _http.GetFromJsonAsync<ServerDashboardDto>("api/admin/dashboard") ?? throw new InvalidOperationException("Empty dashboard response");
    
    public async Task<ServerVersionInfoDto> GetServerVersionInfoAsync()
        => await _http.GetFromJsonAsync<ServerVersionInfoDto>("api/admin/version") ?? throw new InvalidOperationException("Empty server version response");

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

    public async Task<CustomerDto> ApplyCustomerPaymentAsync(Guid id, CustomerPaymentRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/customers/{id}/payments", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerDto>() ?? throw new InvalidOperationException("Empty customer payment response");
    }

    public async Task<IReadOnlyList<CustomerActivityRowDto>> GetCustomerActivityAsync(Guid id, DateTime fromUtc, DateTime toUtc)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        return await GetFromJsonBodyAsync<List<CustomerActivityRowDto>>($"api/customers/{id}/activity?{query}") ?? [];
    }

    public async Task DeleteCustomerAsync(Guid id)
        => (await _http.DeleteAsync($"api/customers/{id}")).EnsureSuccessStatusCode();

    public async Task<ReportSummaryDto> GetReportSummaryAsync(DateTime fromUtc, DateTime toUtc, int? inventoryBucket = null)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}{BuildInventoryBucketQuery(inventoryBucket)}";
        var candidates = new[]
        {
            $"api/reports/summary?{query}",
            $"api/sales/summary?{query}"
        };

        return await GetFromJsonBodyWithFallbackAsync<ReportSummaryDto>(
                candidates,
                "Your server does not expose a reports summary endpoint. Update the server to use financial reports.")
            ?? throw new InvalidOperationException("Empty reports response");
    }

    public async Task<IReadOnlyList<SaleLogEntryDto>> GetSalesLogAsync(DateTime fromUtc, DateTime toUtc, int? inventoryBucket = null)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}{BuildInventoryBucketQuery(inventoryBucket)}";
        return await GetFromJsonBodyAsync<List<SaleLogEntryDto>>($"api/reports/sales-log?{query}") ?? [];
    }

    public async Task<IReadOnlyList<InventoryMovementRowDto>> GetInventoryMovementsAsync(DateTime fromUtc, DateTime toUtc, string locationCode, int? inventoryBucket = null)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}&locationCode={Uri.EscapeDataString(locationCode)}{BuildInventoryBucketQuery(inventoryBucket)}";
        return await GetFromJsonBodyAsync<List<InventoryMovementRowDto>>($"api/reports/inventory-movements?{query}") ?? [];
    }

    public async Task<IReadOnlyList<LowStockRowDto>> GetLowStockAsync(string locationCode, int lookbackDays, int? inventoryBucket = null)
    {
        var query = $"locationCode={Uri.EscapeDataString(locationCode)}&lookbackDays={lookbackDays}{BuildInventoryBucketQuery(inventoryBucket)}";
        return await GetFromJsonBodyAsync<List<LowStockRowDto>>($"api/reports/low-stock?{query}") ?? [];
    }

    public async Task<IReadOnlyList<CustomerReceivablesRowDto>> GetCustomerReceivablesAsync(DateTime fromUtc, DateTime toUtc)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        return await GetFromJsonBodyAsync<List<CustomerReceivablesRowDto>>($"api/reports/customer-receivables?{query}") ?? [];
    }

    public async Task RefundSaleItemAsync(Guid saleId, Guid saleLineId, decimal quantity)
        => (await _http.PostAsJsonAsync($"api/reports/sales/{saleId}/refund-item", new SaleItemRefundRequest(saleLineId, quantity))).EnsureSuccessStatusCode();
    public void Dispose() => _http.Dispose();
    private async Task<T?> GetFromJsonBodyWithFallbackAsync<T>(IReadOnlyList<string> urls, string allNotFoundMessage)
    {
        HttpRequestException? lastHttpError = null;
        Exception? lastParseError = null;

        for (var i = 0; i < urls.Count; i++)
        {
            try
            {
                return await GetFromJsonBodyAsync<T>(urls[i]);
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404 && i < urls.Count - 1)
            {
                lastHttpError = ex;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404)
            {
                throw new HttpRequestException(allNotFoundMessage, ex, ex.StatusCode);
            }
            catch (Exception ex) when ((ex is JsonException or NotSupportedException or FormatException) && i < urls.Count - 1)
            {
                lastParseError = ex;
            }
        }

        if (lastHttpError != null)
            throw lastHttpError;
        if (lastParseError != null)
            throw lastParseError;

        return default;
    }

    private async Task<T?> GetFromJsonBodyAsync<T>(string url)
    {
        using var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        if (stream.CanSeek && stream.Length == 0)
            return default;

        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }

    private static string BuildInventoryBucketQuery(int? inventoryBucket)
        => inventoryBucket is null ? string.Empty : $"&inventoryBucket={Math.Clamp(inventoryBucket.Value, 1, 2)}";
}
