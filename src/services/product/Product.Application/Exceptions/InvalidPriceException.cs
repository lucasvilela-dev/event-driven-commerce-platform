namespace Product.Application.Exceptions;

public sealed class InvalidPriceException(string message) : Exception(message);