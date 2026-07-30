using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(ApplicationUser user, string password);
    PasswordVerificationResult Verify(ApplicationUser user, string password, string hash);
}

public enum PasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded,
}