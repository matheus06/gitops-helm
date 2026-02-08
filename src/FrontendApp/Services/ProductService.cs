using System.Net.Http.Json;
using FrontendApp.Models;

namespace FrontendApp.Services;

public class ProductService : IProductService
{
    private readonly HttpClient _httpClient;

    public ProductService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Product>> GetProductsAsync()
    {
        try
        {
            var products = await _httpClient.GetFromJsonAsync<List<Product>>("api/products");
            return products ?? new List<Product>();
        }
        catch (Exception)
        {
            return new List<Product>();
        }
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Product>($"api/products/{id}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<Product?> CreateProductAsync(CreateProductRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/products", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Product>();
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<Product?> UpdateProductAsync(int id, CreateProductRequest request)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"api/products/{id}", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Product>();
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> DeleteProductAsync(int id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"api/products/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
