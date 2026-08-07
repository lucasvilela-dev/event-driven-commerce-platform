using MediatR;
using Product.Application.Abstractions;
using Product.Application.Exceptions;
using Product.Application.Events;
using Product.Domain.ValueObjects;

namespace Product.Application.Commands.UpdatePrice;

internal sealed class UpdatePriceHandler(
    IProductRepository products,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdatePriceCommand>
{
    public async Task Handle(UpdatePriceCommand request, CancellationToken ct)
    {
        if (request.Price < 0)
        {
            throw new InvalidPriceException("Price must be non-negative.");
        }

        var product = await products.FindByIdAsync(request.ProductId, ct)
            ?? throw new ProductNotFoundException(request.ProductId);

        var newPrice = new Money(request.Price,
            string.IsNullOrWhiteSpace(request.Currency) ? product.Currency : request.Currency);
        product.UpdatePrice(newPrice);

        await products.UpdateAsync(product, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();
        await outbox.EnqueueAsync(
            ProductOutboxFactory.PriceUpdated(product, eventId, occurredAt),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}