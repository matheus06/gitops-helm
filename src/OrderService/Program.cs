using System.Diagnostics.Metrics;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var serviceName = "order-service";

// MongoDB configuration - check Vault file first, then env var, then default
var vaultSecretPath = "/vault/secrets/mongodb";
string mongoConnectionString;
if (File.Exists(vaultSecretPath))
{
    mongoConnectionString = File.ReadAllText(vaultSecretPath).Trim();
}
else
{
    mongoConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
        ?? "mongodb://localhost:27017/microservices";
}
var mongoClient = new MongoClient(mongoConnectionString);
var database = mongoClient.GetDatabase("microservices");
var ordersCollection = database.GetCollection<OrderDocument>("orders");
var countersCollection = database.GetCollection<CounterDocument>("counters");

// Register MongoDB client for health checks
builder.Services.AddSingleton<IMongoClient>(mongoClient);
builder.Services.AddHealthChecks()
    .AddMongoDb(mongoConnectionString, name: "mongodb", timeout: TimeSpan.FromSeconds(3));

// Custom metrics
var meter = new Meter(serviceName, "1.0.0");
var orderRequestCounter = meter.CreateCounter<long>("order_requests_total", description: "Total number of order API requests");
var ordersCreatedCounter = meter.CreateCounter<long>("orders_created_total", description: "Total number of orders created");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otelEndpoint)))
    .WithMetrics(metrics => metrics
        .AddMeter(serviceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otelEndpoint)));

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    logging.AddOtlpExporter(options => options.Endpoint = new Uri(otelEndpoint));
});

var app = builder.Build();
var logger = app.Logger;

app.UseSwagger();
app.UseSwaggerUI();

// Seed data on startup (idempotent)
var seedData = Environment.GetEnvironmentVariable("SEED_DATA") ?? "true";
if (seedData.Equals("true", StringComparison.OrdinalIgnoreCase))
{
    await SeedDataAsync(ordersCollection, countersCollection, logger);
}

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapHealthChecks("/health");

app.MapGet("/api/orders", async () =>
{
    logger.LogInformation("Fetching all orders");
    orderRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "list"));
    var orders = await ordersCollection.Find(_ => true).ToListAsync();
    return orders.Select(o => o.ToOrder());
});

app.MapGet("/api/orders/{id}", async (int id) =>
{
    logger.LogInformation("Fetching order with ID {OrderId}", id);
    orderRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "get"));
    var order = await ordersCollection.Find(o => o.Id == id).FirstOrDefaultAsync();
    return order is not null ? Results.Ok(order.ToOrder()) : Results.NotFound();
});

app.MapGet("/api/orders/customer/{customerId}", async (int customerId) =>
{
    var orders = await ordersCollection.Find(o => o.CustomerId == customerId).ToListAsync();
    return Results.Ok(orders.Select(o => o.ToOrder()));
});

app.MapPost("/api/orders", async (CreateOrderRequest request) =>
{
    orderRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "create"));
    ordersCreatedCounter.Add(1);

    var nextId = await GetNextSequenceValueAsync(countersCollection, "orderId");
    var doc = new OrderDocument
    {
        Id = nextId,
        CustomerId = request.CustomerId,
        Items = request.Items.Select(i => new OrderItemDocument { ProductId = i.ProductId, Quantity = i.Quantity }).ToList(),
        Status = OrderStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
    await ordersCollection.InsertOneAsync(doc);
    return Results.Created($"/api/orders/{doc.Id}", doc.ToOrder());
});

app.MapPut("/api/orders/{id}/status", async (int id, UpdateStatusRequest request) =>
{
    var filter = Builders<OrderDocument>.Filter.Eq(o => o.Id, id);
    var update = Builders<OrderDocument>.Update.Set(o => o.Status, request.Status);
    var result = await ordersCollection.FindOneAndUpdateAsync(filter, update,
        new FindOneAndUpdateOptions<OrderDocument> { ReturnDocument = ReturnDocument.After });
    if (result is null) return Results.NotFound();
    return Results.Ok(result.ToOrder());
});

app.MapDelete("/api/orders/{id}", async (int id) =>
{
    var result = await ordersCollection.DeleteOneAsync(o => o.Id == id);
    if (result.DeletedCount == 0) return Results.NotFound();
    return Results.NoContent();
});

app.Run();

static async Task<int> GetNextSequenceValueAsync(IMongoCollection<CounterDocument> counters, string name)
{
    var filter = Builders<CounterDocument>.Filter.Eq(c => c.Id, name);
    var update = Builders<CounterDocument>.Update.Inc(c => c.Seq, 1);
    var options = new FindOneAndUpdateOptions<CounterDocument>
    {
        IsUpsert = true,
        ReturnDocument = ReturnDocument.After
    };
    var result = await counters.FindOneAndUpdateAsync(filter, update, options);
    return result.Seq;
}

static async Task SeedDataAsync(
    IMongoCollection<OrderDocument> orders,
    IMongoCollection<CounterDocument> counters,
    ILogger logger)
{
    var count = await orders.CountDocumentsAsync(_ => true);
    if (count > 0)
    {
        logger.LogInformation("Orders collection already has {Count} items, skipping seed", count);
        return;
    }

    logger.LogInformation("Seeding orders collection");

    var seedOrders = new List<OrderDocument>
    {
        new()
        {
            Id = 1,
            CustomerId = 101,
            Items = new List<OrderItemDocument>
            {
                new() { ProductId = 1, Quantity = 2 },
                new() { ProductId = 2, Quantity = 1 }
            },
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        },
        new()
        {
            Id = 2,
            CustomerId = 102,
            Items = new List<OrderItemDocument>
            {
                new() { ProductId = 3, Quantity = 1 }
            },
            Status = OrderStatus.Processing,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        },
        new()
        {
            Id = 3,
            CustomerId = 103,
            Items = new List<OrderItemDocument>
            {
                new() { ProductId = 4, Quantity = 3 },
                new() { ProductId = 1, Quantity = 2 }
            },
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        }
    };

    await orders.InsertManyAsync(seedOrders);

    // Initialize counter to 3 (last seeded ID)
    var filter = Builders<CounterDocument>.Filter.Eq(c => c.Id, "orderId");
    var update = Builders<CounterDocument>.Update.SetOnInsert(c => c.Seq, 3);
    await counters.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

    logger.LogInformation("Seeded {Count} orders", seedOrders.Count);
}

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

public class OrderDocument
{
    [BsonId]
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public List<OrderItemDocument> Items { get; set; } = new();
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order ToOrder() => new(Id, CustomerId, Items.Select(i => new OrderItem(i.ProductId, i.Quantity)).ToArray(), Status, CreatedAt);
}

public class OrderItemDocument
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CounterDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public int Seq { get; set; }
}
