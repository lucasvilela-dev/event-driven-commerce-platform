using Identity.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using IdentityDomain = Identity.Domain.Entities;
using AppResult = Identity.Application.Abstractions.PasswordVerificationResult;
using AspNetResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace Identity.Infrastructure.Services;

internal sealed class PasswordHasherAdapter : IPasswordHasher
{
    private readonly IPasswordHasher<IdentityDomain.ApplicationUser> _inner = new PasswordHasher<IdentityDomain.ApplicationUser>();

    public string Hash(IdentityDomain.ApplicationUser user, string password) => _inner.HashPassword(user, password);

    public AppResult Verify(IdentityDomain.ApplicationUser user, string password, string hash)
    {
        var result = _inner.VerifyHashedPassword(user, hash, password);
        return result switch
        {
            AspNetResult.Success => AppResult.Success,
            AspNetResult.SuccessRehashNeeded => AppResult.SuccessRehashNeeded,
            _ => AppResult.Failed,
        };
    }
}