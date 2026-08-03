namespace Product.Application.Exceptions;

public sealed class ProductNotFoundException(Guid id) : Exception(
    $"Product with id '{id}' was not found.")
{
    public Guid Id { get; } = id;
}