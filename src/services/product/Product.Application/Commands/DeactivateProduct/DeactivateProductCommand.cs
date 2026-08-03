using MediatR;

namespace Product.Application.Commands.DeactivateProduct;

public sealed record DeactivateProductCommand(Guid ProductId) : IRequest;