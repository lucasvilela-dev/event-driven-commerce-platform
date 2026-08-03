using Microsoft.EntityFrameworkCore;
using Product.Application.Abstractions;
using Product.Infrastructure.Persistence;

namespace Product.Infrastructure.Persistence.Repositories;

internal sealed class OutboxRepository(ProductDbContext context) : IOutboxRepository
{
    private readonly ProductDbContext _context = context;

    public async Task EnqueueAsync(OutboxMessageDescriptor descriptor, CancellationToken ct = default)
    {
        await _context.Outbox.AddAsync(new OutboxMessage
        {
            Id = descriptor.Id,
            AggregateType = descriptor.AggregateType,
            AggregateId = descriptor.AggregateId,
            EventType = descriptor.EventType,
            Topic = descriptor.Topic,
            PartitionKey = descriptor.PartitionKey,
            PayloadJson = descriptor.PayloadJson,
            OccurredAt = descriptor.OccurredAt,
            RetryCount = 0,
        }, ct);
    }

    public async Task<IReadOnlyList<OutboxMessageDescriptor>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var rows = await _context.Outbox
            .FromSqlRaw("""
                SELECT * FROM "outbox"
                WHERE "PublishedAt" IS NULL
                ORDER BY "OccurredAt"
                FOR UPDATE SKIP LOCKED
                LIMIT {0}
                """, batchSize)
            .AsTracking()
            .ToListAsync(ct);

        return rows
            .Select(r => new OutboxMessageDescriptor(
                r.Id, r.AggregateType, r.AggregateId, r.EventType, r.Topic, r.PartitionKey,
                r.PayloadJson, r.OccurredAt))
            .ToList();
    }

    public async Task MarkPublishedAsync(Guid id, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "outbox" SET "PublishedAt" = {DateTimeOffset.UtcNow} WHERE "Id" = {id}""", ct);
    }
}