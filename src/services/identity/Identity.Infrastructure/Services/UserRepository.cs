using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Services;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    private DbSet<ApplicationUser> Users => context.Users;

    public Task<ApplicationUser?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => Users.Include(u => u.Roles).ThenInclude(ur => ur!.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default)
        => Users.Include(u => u.Roles).ThenInclude(ur => ur!.Role)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task AddAsync(ApplicationUser user, CancellationToken ct = default)
    {
        await Users.AddAsync(user, ct);
    }

    public Task UpdateAsync(ApplicationUser user, CancellationToken ct = default)
    {
        Users.Update(user);
        return Task.CompletedTask;
    }
}