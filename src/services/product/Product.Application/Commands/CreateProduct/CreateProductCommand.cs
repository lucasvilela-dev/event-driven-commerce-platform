using MediatR;
using Product.Domain.ValueObjects;

namespace Product.Application.Commands.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string Currency,
    string Category,
    bool Featured = false,
    int Priority = 0) : IRequest<CreateProductResult>;

public sealed record CreateProductResult(Guid Id);