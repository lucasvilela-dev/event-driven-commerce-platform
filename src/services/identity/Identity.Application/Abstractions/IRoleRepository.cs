using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public interface IRoleRepository
{
    Task<Role?> FindByNameAsync(string normalizedName, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> FindByNamesAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
}