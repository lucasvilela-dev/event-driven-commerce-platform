using MediatR;
using Product.Domain.ValueObjects;

namespace Product.Application.Commands.UpdatePrice;

public sealed record UpdatePriceCommand(
    Guid ProductId,
    decimal Price,
    string Currency) : IRequest;