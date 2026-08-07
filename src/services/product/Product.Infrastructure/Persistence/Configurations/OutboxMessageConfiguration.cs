using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Product.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id).HasColumnType("uuid");
        builder.Property(o => o.AggregateType).HasMaxLength(64).IsRequired();
        builder.Property(o => o.AggregateId).HasColumnType("uuid").IsRequired();
        builder.Property(o => o.EventType).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Topic).HasMaxLength(128).IsRequired();
        builder.Property(o => o.PartitionKey).HasMaxLength(128).IsRequired();
        builder.Property(o => o.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(o => o.PublishedAt).HasColumnType("timestamp with time zone");
        builder.Property(o => o.RetryCount).IsRequired();

        builder.HasIndex(o => o.PublishedAt);
        builder.HasIndex(o => new { o.PublishedAt, o.OccurredAt });
    }
}