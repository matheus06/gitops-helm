using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var serviceName = "order-service";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otelEndpoint)));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

var orders = new List<Order>
{
    new(1, 101, new[] { new OrderItem(1, 2), new OrderItem(2, 1) }, OrderStatus.Completed, DateTime.UtcNow.AddDays(-5)),
    new(2, 102, new[] { new OrderItem(3, 1) }, OrderStatus.Processing, DateTime.UtcNow.AddDays(-1)),
    new(3, 103, new[] { new OrderItem(4, 3), new OrderItem(5, 2) }, OrderStatus.Pending, DateTime.UtcNow.AddDays(-2))
};

var nextId = 3;

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "OrderService" }));

app.MapGet("/api/orders", () => orders);

app.MapGet("/api/orders/{id}", (int id) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);
    return order is not null ? Results.Ok(order) : Results.NotFound();
});

app.MapGet("/api/orders/customer/{customerId}", (int customerId) =>
{
    var customerOrders = orders.Where(o => o.CustomerId == customerId).ToList();
    return Results.Ok(customerOrders);
});

app.MapPost("/api/orders", (CreateOrderRequest request) =>
{
    var order = new Order(nextId++, request.CustomerId, request.Items, OrderStatus.Pending, DateTime.UtcNow);
    orders.Add(order);
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapPut("/api/orders/{id}/status", (int id, UpdateStatusRequest request) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);
    if (order is null) return Results.NotFound();

    var index = orders.IndexOf(order);
    orders[index] = order with { Status = request.Status };
    return Results.Ok(orders[index]);
});

app.MapDelete("/api/orders/{id}", (int id) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);
    if (order is null) return Results.NotFound();
    orders.Remove(order);
    return Results.NoContent();
});

app.Run();

public record Order(int Id, int CustomerId, OrderItem[] Items, OrderStatus Status, DateTime CreatedAt);
public record OrderItem(int ProductId, int Quantity);
public record CreateOrderRequest(int CustomerId, OrderItem[] Items);
public record UpdateStatusRequest(OrderStatus Status);

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Completed,
    Cancelled
}
