using Identity.Domain.Entities;
using Identity.Domain.ValueObjects;

namespace Identity.Application.Abstractions;

public interface ITokenIssuer
{
    TokenPair Issue(ApplicationUser user, IReadOnlyCollection<string> roles);
}