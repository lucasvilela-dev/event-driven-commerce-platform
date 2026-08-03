using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Product.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product.Domain.Entities.ProductAggregate>
{
    public void Configure(EntityTypeBuilder<Product.Domain.Entities.ProductAggregate> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnType("uuid");
        builder.Property(p => p.Name).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2048).IsRequired();
        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.PriceAmount).HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Active).IsRequired();
        builder.Property(p => p.Featured).IsRequired();
        builder.Property(p => p.Priority).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Category);
        builder.HasIndex(p => new { p.Featured, p.Priority });
    }
}