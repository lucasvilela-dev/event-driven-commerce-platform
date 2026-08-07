using Product.Application.Abstractions;
using Product.Infrastructure.Persistence;

namespace Product.Infrastructure.Services;

internal sealed class UnitOfWork(ProductDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}