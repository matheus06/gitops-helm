using FrontendApp.Models;

namespace FrontendApp.Services;

public interface IProductService
{
    Task<List<Product>> GetProductsAsync();
    Task<Product?> GetProductAsync(int id);
    Task<Product?> CreateProductAsync(CreateProductRequest request);
    Task<Product?> UpdateProductAsync(int id, CreateProductRequest request);
    Task<bool> DeleteProductAsync(int id);
}
