namespace Product.Application.Exceptions;

public sealed class DuplicateSkuException(string sku) : Exception(
    $"A product with SKU '{sku}' already exists.")
{
    public string Sku { get; } = sku;
}