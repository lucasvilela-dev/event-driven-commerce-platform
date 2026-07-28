using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Services;

internal sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    private DbSet<Role> Roles => context.Roles;

    public Task<Role?> FindByNameAsync(string normalizedName, CancellationToken ct = default)
        => Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, ct);

    public async Task<IReadOnlyList<Role>> FindByNamesAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken ct = default)
    {
        var list = await Roles.Where(r => normalizedNames.Contains(r.NormalizedName)).ToListAsync(ct);
        return list;
    }

    public async Task AddAsync(Role role, CancellationToken ct = default)
    {
        await Roles.AddAsync(role, ct);
    }
}