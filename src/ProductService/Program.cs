var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.MapGet("/api/products", () => products);

app.MapGet("/api/products/{id}", (int id) =>
{
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
