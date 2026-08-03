using Microsoft.EntityFrameworkCore;
using Product.Application.Abstractions;
using Product.Domain.Entities;

namespace Product.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(ProductDbContext context) : IProductRepository
{
    private readonly ProductDbContext _context = context;

    public Task<ProductAggregate?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.Products.AsTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<ProductAggregate?> FindBySkuAsync(string sku, CancellationToken ct = default) =>
        _context.Products.AsTracking().FirstOrDefaultAsync(p => p.Sku == sku, ct);

    public Task AddAsync(ProductAggregate product, CancellationToken ct = default) =>
        _context.Products.AddAsync(product, ct).AsTask();

    public Task UpdateAsync(ProductAggregate product, CancellationToken ct = default)
    {
        _context.Products.Update(product);
        return Task.CompletedTask;
    }
}