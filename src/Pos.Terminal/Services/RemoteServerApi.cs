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

    public async Task DeleteCustomerAsync(Guid id)
        => (await _http.DeleteAsync($"api/customers/{id}")).EnsureSuccessStatusCode();

    public async Task<ReportSummaryDto> GetReportSummaryAsync(DateTime fromUtc, DateTime toUtc)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        var candidates = new[]
        {
            $"api/reports/summary?{query}",
            $"api/sales/summary?{query}"
        };

        return await GetFromJsonWithFallbackAsync<ReportSummaryDto>(
                candidates,
                "Your server does not expose a reports summary endpoint. Update the server to use financial reports.")
            ?? throw new InvalidOperationException("Empty reports response");
    }

    public async Task<IReadOnlyList<ServerSalesExportRowDto>> GetSalesExportAsync(DateTime fromUtc, DateTime toUtc)
    {
         // Prefer sales-log because it is generated from the same dataset used by the Sales Register UI,
        // which avoids stale/partial implementations of legacy export endpoints.
        try
        {
            return await GetSalesExportFromSalesLogAsync(fromUtc, toUtc);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404)
        {
            // Older servers may not expose sales-log; fall back to legacy export endpoints.
        }
        catch (JsonException)
        {
            // Some deployed servers have returned malformed sales-log JSON for particular
            // periods. The flat export endpoints are simpler and still period-scoped.
        }
        catch (NotSupportedException)
        {
            // Treat unsupported/non-JSON sales-log responses the same as malformed JSON.
        }

        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        var candidates = new[]
        {
            $"api/reports/sales-export?{query}",
            $"api/sales/export?{query}"
        };

         return await GetFromJsonWithFallbackAsync<List<ServerSalesExportRowDto>>(
            candidates,
            "Your server does not expose a sales export endpoint. Update the server to use sales reports.") ?? [];
    }


    private async Task<IReadOnlyList<ServerSalesExportRowDto>> GetSalesExportFromSalesLogAsync(DateTime fromUtc, DateTime toUtc)
    {
        var salesLog = await GetSalesLogAsync(fromUtc, toUtc);
        return salesLog
            .Select(entry =>
            {
                var vat = Math.Max(0m, entry.Total - entry.Subtotal);
                return new ServerSalesExportRowDto(
                    entry.SoldAtUtc,
                    entry.ReceiptNo,
                    "Completed",
                    entry.PaymentType,
                    string.Empty,
                    entry.Subtotal,
                    vat,
                    entry.Total);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SaleLogEntryDto>> GetSalesLogAsync(DateTime fromUtc, DateTime toUtc)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";
        return await _http.GetFromJsonAsync<List<SaleLogEntryDto>>($"api/reports/sales-log?{query}") ?? [];
    }

    public async Task<IReadOnlyList<InventoryMovementRowDto>> GetInventoryMovementsAsync(DateTime fromUtc, DateTime toUtc, string locationCode)
    {
        var query = $"fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}&locationCode={Uri.EscapeDataString(locationCode)}";
        return await _http.GetFromJsonAsync<List<InventoryMovementRowDto>>($"api/reports/inventory-movements?{query}") ?? [];
    }

    public async Task<IReadOnlyList<LowStockRowDto>> GetLowStockAsync(string locationCode, int lookbackDays)
    {
        var query = $"locationCode={Uri.EscapeDataString(locationCode)}&lookbackDays={lookbackDays}";
        return await _http.GetFromJsonAsync<List<LowStockRowDto>>($"api/reports/low-stock?{query}") ?? [];
    }

    public async Task RefundSaleItemAsync(Guid saleId, Guid saleLineId, decimal quantity)
        => (await _http.PostAsJsonAsync($"api/reports/sales/{saleId}/refund-item", new SaleItemRefundRequest(saleLineId, quantity))).EnsureSuccessStatusCode();
    public void Dispose() => _http.Dispose();
    private async Task<T?> GetFromJsonWithFallbackAsync<T>(IReadOnlyList<string> urls, string allNotFoundMessage)
    {
        HttpRequestException? lastHttpError = null;
        Exception? lastParseError = null;
        for (var i = 0; i < urls.Count; i++)
        {
            try
            {
                return await _http.GetFromJsonAsync<T>(urls[i]);
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404 && i < urls.Count - 1)
            {
                lastHttpError = ex;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 404)
            {
                throw new HttpRequestException(allNotFoundMessage, ex, ex.StatusCode);
            }
            catch (JsonException ex) when (i < urls.Count - 1)
            {
                lastParseError = ex;
            }
            catch (NotSupportedException ex) when (i < urls.Count - 1)
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
}
