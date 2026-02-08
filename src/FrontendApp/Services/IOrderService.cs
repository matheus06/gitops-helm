using FrontendApp.Models;

namespace FrontendApp.Services;

public interface IOrderService
{
    Task<List<Order>> GetOrdersAsync();
    Task<Order?> GetOrderAsync(int id);
    Task<List<Order>> GetOrdersByCustomerAsync(int customerId);
    Task<Order?> CreateOrderAsync(CreateOrderRequest request);
    Task<Order?> UpdateOrderStatusAsync(int id, OrderStatus status);
    Task<bool> DeleteOrderAsync(int id);
}
