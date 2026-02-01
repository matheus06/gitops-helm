using System.Diagnostics.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://otel-collector:4317";
var serviceName = "product-service";

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

app.UseSwagger();
app.UseSwaggerUI();

var products = new List<Product>
{
    new(1, "Laptop", 999.99m, 50),
    new(2, "Mouse", 29.99m, 200),
    new(3, "Keyboard", 79.99m, 150),
    new(4, "Monitor", 199.99m, 75)
};

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "ProductService" }));

app.MapGet("/api/products", () =>
{
    productRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "list"));
    return products;
});

app.MapGet("/api/products/{id}", (int id) =>
{
    productRequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "get"));
    productViewCounter.Add(1, new KeyValuePair<string, object?>("product_id", id.ToString()));
    var product = products.FirstOrDefault(p => p.Id == id);
    return product is not null ? Results.Ok(product) : Results.NotFound();
});

app.MapPost("/api/products", (Product product) =>
{
    products.Add(product);
    return Results.Created($"/api/products/{product.Id}", product);
});

app.MapPut("/api/products/{id}", (int id, Product updated) =>
{
    var index = products.FindIndex(p => p.Id == id);
    if (index == -1) return Results.NotFound();
    products[index] = updated;
    return Results.Ok(updated);
});

app.MapDelete("/api/products/{id}", (int id) =>
{
    var product = products.FirstOrDefault(p => p.Id == id);
    if (product is null) return Results.NotFound();
    products.Remove(product);
    return Results.NoContent();
});

app.Run();

public record Product(int Id, string Name, decimal Price, int Stock);
