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

var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var serviceName = "product-service";

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
var productsCollection = database.GetCollection<ProductDocument>("products");
var countersCollection = database.GetCollection<CounterDocument>("counters");

// Register MongoDB client for health checks
builder.Services.AddSingleton<IMongoClient>(mongoClient);
builder.Services.AddHealthChecks()
    .AddMongoDb(mongoConnectionString, name: "mongodb", timeout: TimeSpan.FromSeconds(3));

// Custom metrics
var meter = new Meter(serviceName, "1.0.0");
var productRequestCounter = meter.CreateCounter<long>("product_requests_total", description: "Total number of product API requests");
var productViewCounter = meter.CreateCounter<long>("product_views_total", description: "Total number of individual product views");

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
    await SeedDataAsync(productsCollection, countersCollection, logger);
}

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapHealthChecks("/health");

app.MapGet("/api/products", async () =>
{
    logger.LogInformation("Fetching all products");
    productRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "list"));
    var products = await productsCollection.Find(_ => true).ToListAsync();
    return products.Select(p => p.ToProduct());
});

app.MapGet("/api/products/{id}", async (int id) =>
{
    logger.LogInformation("Fetching product with ID {ProductId}", id);
    productRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "get"));
    productViewCounter.Add(1, new KeyValuePair<string, object?>("product_id", id.ToString()));
    var product = await productsCollection.Find(p => p.Id == id).FirstOrDefaultAsync();
    return product is not null ? Results.Ok(product.ToProduct()) : Results.NotFound();
});

app.MapPost("/api/products", async (Product product) =>
{
    var nextId = await GetNextSequenceValueAsync(countersCollection, "productId");
    var doc = new ProductDocument
    {
        Id = nextId,
        Name = product.Name,
        Price = product.Price,
        Stock = product.Stock
    };
    await productsCollection.InsertOneAsync(doc);
    var created = doc.ToProduct();
    return Results.Created($"/api/products/{created.Id}", created);
});

app.MapPut("/api/products/{id}", async (int id, Product updated) =>
{
    var filter = Builders<ProductDocument>.Filter.Eq(p => p.Id, id);
    var update = Builders<ProductDocument>.Update
        .Set(p => p.Name, updated.Name)
        .Set(p => p.Price, updated.Price)
        .Set(p => p.Stock, updated.Stock);
    var result = await productsCollection.UpdateOneAsync(filter, update);
    if (result.MatchedCount == 0) return Results.NotFound();
    return Results.Ok(updated with { Id = id });
});

app.MapDelete("/api/products/{id}", async (int id) =>
{
    var result = await productsCollection.DeleteOneAsync(p => p.Id == id);
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
    IMongoCollection<ProductDocument> products,
    IMongoCollection<CounterDocument> counters,
    ILogger logger)
{
    var count = await products.CountDocumentsAsync(_ => true);
    if (count > 0)
    {
        logger.LogInformation("Products collection already has {Count} items, skipping seed", count);
        return;
    }

    logger.LogInformation("Seeding products collection");

    var seedProducts = new List<ProductDocument>
    {
        new() { Id = 1, Name = "Laptop", Price = 999.99m, Stock = 50 },
        new() { Id = 2, Name = "Mouse", Price = 29.99m, Stock = 200 },
        new() { Id = 3, Name = "Keyboard", Price = 79.99m, Stock = 150 },
        new() { Id = 4, Name = "Monitor", Price = 199.99m, Stock = 75 }
    };

    await products.InsertManyAsync(seedProducts);

    // Initialize counter to 4 (last seeded ID)
    var filter = Builders<CounterDocument>.Filter.Eq(c => c.Id, "productId");
    var update = Builders<CounterDocument>.Update.SetOnInsert(c => c.Seq, 4);
    await counters.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

    logger.LogInformation("Seeded {Count} products", seedProducts.Count);
}

public record Product(int Id, string Name, decimal Price, int Stock);

public class ProductDocument
{
    [BsonId]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Price { get; set; }
    public int Stock { get; set; }

    public Product ToProduct() => new(Id, Name, Price, Stock);
}

public class CounterDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public int Seq { get; set; }
}
