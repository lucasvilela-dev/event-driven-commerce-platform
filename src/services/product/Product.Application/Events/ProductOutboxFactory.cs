using System.Text.Json;
using Product.Application.Abstractions;
using Product.Domain.Entities;

namespace Product.Application.Events;

public static class ProductOutboxFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static OutboxMessageDescriptor ProductCreated(
        ProductAggregate product, Guid eventId, DateTimeOffset occurredAt)
    {
        var payload = new
        {
            event_id = eventId.ToString(),
            aggregate_id = product.Id.ToString(),
            occurred_at = occurredAt.ToUnixTimeMilliseconds(),
            name = product.Name,
            description = product.Description,
            sku = product.Sku,
            price = product.PriceAmount,
            currency = product.Currency,
            category = product.Category,
            featured = product.Featured,
            priority = product.Priority,
            active = product.Active,
        };
        return Descriptor(eventId, product.Id, ProductEvents.EventTypes.ProductCreated,
            ProductEvents.Topics.Created, payload, occurredAt);
    }

    public static OutboxMessageDescriptor PriceUpdated(
        ProductAggregate product, Guid eventId, DateTimeOffset occurredAt)
    {
        var payload = new
        {
            event_id = eventId.ToString(),
            aggregate_id = product.Id.ToString(),
            occurred_at = occurredAt.ToUnixTimeMilliseconds(),
            price = product.PriceAmount,
            currency = product.Currency,
        };
        return Descriptor(eventId, product.Id, ProductEvents.EventTypes.ProductPriceUpdated,
            ProductEvents.Topics.PriceUpdated, payload, occurredAt);
    }

    public static OutboxMessageDescriptor Activated(
        ProductAggregate product, Guid eventId, DateTimeOffset occurredAt)
    {
        var payload = new
        {
            event_id = eventId.ToString(),
            aggregate_id = product.Id.ToString(),
            occurred_at = occurredAt.ToUnixTimeMilliseconds(),
        };
        return Descriptor(eventId, product.Id, ProductEvents.EventTypes.ProductActivated,
            ProductEvents.Topics.Activated, payload, occurredAt);
    }

    public static OutboxMessageDescriptor Deactivated(
        ProductAggregate product, Guid eventId, DateTimeOffset occurredAt)
    {
        var payload = new
        {
            event_id = eventId.ToString(),
            aggregate_id = product.Id.ToString(),
            occurred_at = occurredAt.ToUnixTimeMilliseconds(),
        };
        return Descriptor(eventId, product.Id, ProductEvents.EventTypes.ProductDeactivated,
            ProductEvents.Topics.Deactivated, payload, occurredAt);
    }

    private static OutboxMessageDescriptor Descriptor(
        Guid eventId, Guid aggregateId, string eventType, string topic,
        object payload, DateTimeOffset occurredAt)
    {
        return new OutboxMessageDescriptor(
            Id: eventId,
            AggregateType: ProductEvents.AggregateType,
            AggregateId: aggregateId,
            EventType: eventType,
            Topic: topic,
            PartitionKey: aggregateId.ToString(),
            PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
            OccurredAt: occurredAt);
    }
}