using System.Net.Http.Json;
using FrontendApp.Models;

namespace FrontendApp.Services;

public class OrderService : IOrderService
{
    private readonly HttpClient _httpClient;

    public OrderService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<Order>> GetOrdersAsync()
    {
        try
        {
            var orders = await _httpClient.GetFromJsonAsync<List<Order>>("api/orders");
            return orders ?? new List<Order>();
        }
        catch (Exception)
        {
            return new List<Order>();
        }
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Order>($"api/orders/{id}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<Order>> GetOrdersByCustomerAsync(int customerId)
    {
        try
        {
            var orders = await _httpClient.GetFromJsonAsync<List<Order>>($"api/orders/customer/{customerId}");
            return orders ?? new List<Order>();
        }
        catch (Exception)
        {
            return new List<Order>();
        }
    }

    public async Task<Order?> CreateOrderAsync(CreateOrderRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/orders", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Order>();
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<Order?> UpdateOrderStatusAsync(int id, OrderStatus status)
    {
        try
        {
            var request = new UpdateStatusRequest { Status = status };
            var response = await _httpClient.PutAsJsonAsync($"api/orders/{id}/status", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Order>();
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"api/orders/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
