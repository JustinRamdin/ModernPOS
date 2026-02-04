using System.Net.Http.Json;
using Pos.Terminal.Models;

namespace Pos.Terminal.Services;

public class PosApi
{
    private readonly HttpClient _http;

    // For now hardcode localhost. Later we load from config file.
    public PosApi()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5055/")
        };
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var list = await _http.GetFromJsonAsync<List<ProductDto>>("api/products");
        return list ?? new List<ProductDto>();
    }

    public async Task<CheckoutResponse?> CheckoutAsync(CheckoutRequest req)
    {
        var res = await _http.PostAsJsonAsync("api/sales/checkout", req);
        if (!res.IsSuccessStatusCode)
        {
            var msg = await res.Content.ReadAsStringAsync();
            throw new Exception($"Checkout failed: {(int)res.StatusCode} {msg}");
        }

        return await res.Content.ReadFromJsonAsync<CheckoutResponse>();
    }
}
