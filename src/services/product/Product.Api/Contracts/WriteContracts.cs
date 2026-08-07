namespace Product.Api.Contracts;

public sealed record CreateProductRequest(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string Currency,
    string Category,
    bool Featured = false,
    int Priority = 0);

public sealed record CreateProductResponse(Guid Id);

public sealed record UpdatePriceRequest(decimal Price, string Currency);