using MediatR;
using Product.Application.Abstractions;
using Product.Application.Events;
using Product.Application.Exceptions;

namespace Product.Application.Commands.DeactivateProduct;

internal sealed class DeactivateProductHandler(
    IProductRepository products,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork) : IRequestHandler<DeactivateProductCommand>
{
    public async Task Handle(DeactivateProductCommand request, CancellationToken ct)
    {
        var product = await products.FindByIdAsync(request.ProductId, ct)
            ?? throw new ProductNotFoundException(request.ProductId);

        product.Deactivate();
        await products.UpdateAsync(product, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();
        await outbox.EnqueueAsync(
            ProductOutboxFactory.Deactivated(product, eventId, occurredAt),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}