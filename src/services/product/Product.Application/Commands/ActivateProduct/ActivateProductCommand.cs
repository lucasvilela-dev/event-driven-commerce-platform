using MediatR;

namespace Product.Application.Commands.ActivateProduct;

public sealed record ActivateProductCommand(Guid ProductId) : IRequest;