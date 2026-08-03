namespace Product.Application.Abstractions;

public interface IOutboxRepository
{
    Task EnqueueAsync(OutboxMessageDescriptor descriptor, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessageDescriptor>> GetPendingAsync(int batchSize, CancellationToken ct = default);
    Task MarkPublishedAsync(Guid id, CancellationToken ct = default);
}

public sealed record OutboxMessageDescriptor(
    Guid Id,
    string AggregateType,
    Guid AggregateId,
    string EventType,
    string Topic,
    string PartitionKey,
    string PayloadJson,
    DateTimeOffset OccurredAt);