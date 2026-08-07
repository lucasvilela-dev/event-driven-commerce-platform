using Product.Domain.Entities;

namespace Product.Application.Abstractions;

public interface IProductRepository
{
    Task<ProductAggregate?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductAggregate?> FindBySkuAsync(string sku, CancellationToken ct = default);
    Task AddAsync(ProductAggregate product, CancellationToken ct = default);
    Task UpdateAsync(ProductAggregate product, CancellationToken ct = default);
}