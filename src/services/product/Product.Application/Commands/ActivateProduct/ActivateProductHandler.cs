using MediatR;
using Product.Application.Abstractions;
using Product.Application.Events;
using Product.Application.Exceptions;

namespace Product.Application.Commands.ActivateProduct;

internal sealed class ActivateProductHandler(
    IProductRepository products,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork) : IRequestHandler<ActivateProductCommand>
{
    public async Task Handle(ActivateProductCommand request, CancellationToken ct)
    {
        var product = await products.FindByIdAsync(request.ProductId, ct)
            ?? throw new ProductNotFoundException(request.ProductId);

        product.Activate();
        await products.UpdateAsync(product, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        var eventId = Guid.CreateVersion7();
        await outbox.EnqueueAsync(
            ProductOutboxFactory.Activated(product, eventId, occurredAt),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
    }
}