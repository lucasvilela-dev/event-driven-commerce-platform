using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public static readonly Guid CustomerRoleId = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001");
    public static readonly Guid AdminRoleId = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000002");

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(128).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(128).IsRequired();

        builder.HasIndex(r => r.NormalizedName).IsUnique();

        builder.HasData(
            new Role { Id = CustomerRoleId, Name = "customer", NormalizedName = "CUSTOMER" },
            new Role { Id = AdminRoleId, Name = "admin", NormalizedName = "ADMIN" }
        );
    }
}