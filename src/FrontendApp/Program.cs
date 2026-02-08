using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FrontendApp;
using FrontendApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Get API URLs from configuration or environment
var productServiceUrl = builder.Configuration["ApiSettings:ProductServiceUrl"]
    ?? "http://localhost:8080";
var orderServiceUrl = builder.Configuration["ApiSettings:OrderServiceUrl"]
    ?? "http://localhost:8081";

// Register HttpClient for ProductService
builder.Services.AddHttpClient<IProductService, ProductService>(client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
});

// Register HttpClient for OrderService
builder.Services.AddHttpClient<IOrderService, OrderService>(client =>
{
    client.BaseAddress = new Uri(orderServiceUrl);
});

await builder.Build().RunAsync();
