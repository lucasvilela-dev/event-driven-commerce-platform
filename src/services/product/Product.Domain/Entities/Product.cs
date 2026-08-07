using Product.Domain.ValueObjects;

namespace Product.Domain.Entities;

public sealed class ProductAggregate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public decimal PriceAmount { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public string Category { get; private set; } = string.Empty;
    public bool Active { get; private set; }
    public bool Featured { get; private set; }
    public int Priority { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Money Price => new(PriceAmount, Currency);

    private ProductAggregate() { }

    public static ProductAggregate CreateNew(
        Guid id,
        string name,
        string description,
        string sku,
        Money price,
        string category,
        bool featured = false,
        int priority = 0,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("SKU is required.", nameof(sku));
        }

        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new ProductAggregate
        {
            Id = id,
            Name = name,
            Description = description ?? string.Empty,
            Sku = sku,
            PriceAmount = price.Amount,
            Currency = price.Currency,
            Category = category ?? string.Empty,
            Active = true,
            Featured = featured,
            Priority = priority,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdatePrice(Money newPrice)
    {
        if (newPrice.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPrice), "Price must be non-negative.");
        }
        PriceAmount = newPrice.Amount;
        Currency = newPrice.Currency;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        if (Active)
        {
            return;
        }
        Active = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}