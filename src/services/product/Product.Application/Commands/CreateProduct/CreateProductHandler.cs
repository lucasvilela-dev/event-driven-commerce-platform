using MediatR;
using Product.Application.Abstractions;
using Product.Application.Exceptions;
using Product.Application.Events;
using Product.Domain.Entities;
using Product.Domain.ValueObjects;

namespace Product.Application.Commands.CreateProduct;

internal sealed class CreateProductHandler(
    IProductRepository products,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand request, CancellationToken ct)
    {
        if (request.Price < 0)
        {
            throw new InvalidPriceException("Price must be non-negative.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Product name is required.", nameof(request.Name));
        }
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            throw new ArgumentException("SKU is required.", nameof(request.Sku));
        }

        var existing = await products.FindBySkuAsync(request.Sku, ct);
        if (existing is not null)
        {
            throw new DuplicateSkuException(request.Sku);
        }

        var productId = Guid.CreateVersion7();
        var price = new Money(request.Price, string.IsNullOrWhiteSpace(request.Currency) ? "BRL" : request.Currency);
        var product = ProductAggregate.CreateNew(
            id: productId,
            name: request.Name,
            description: request.Description,
            sku: request.Sku,
            price: price,
            category: request.Category,
            featured: request.Featured,
            priority: request.Priority);

        await products.AddAsync(product, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();
        await outbox.EnqueueAsync(
            ProductOutboxFactory.ProductCreated(product, eventId, occurredAt),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
        return new CreateProductResult(product.Id);
    }
}