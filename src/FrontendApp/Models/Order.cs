namespace FrontendApp.Models;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrderItem
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled
}

public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class UpdateStatusRequest
{
    public OrderStatus Status { get; set; }
}
