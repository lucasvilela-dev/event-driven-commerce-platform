namespace Product.Domain.ValueObjects;

public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amount must be non-negative.");
        }
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency code is required.", nameof(currency));
        }
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public long Cents => (long)Math.Round(Amount * 100m);

    public static Money Zero(string currency) => new(0m, currency);

    public Money With(decimal amount) => new(amount, Currency);

    public bool Equals(Money? other) =>
        other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => Equals(obj as Money);
    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public static bool operator ==(Money? left, Money? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(Money? left, Money? right) => !(left == right);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}