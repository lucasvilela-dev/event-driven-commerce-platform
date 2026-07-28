using Identity.Application.Abstractions;
using Identity.Application.Exceptions;
using Identity.Application.Services;
using MediatR;

namespace Identity.Application.Commands.Login;

internal sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITokenIssuer tokenIssuer,
    IUnitOfWork unitOfWork) : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var user = await users.FindByEmailAsync(normalizedEmail, ct);
        if (user is null)
        {
            throw new InvalidCredentialsException();
        }

        var verification = hasher.Verify(user, request.Password, user.PasswordHash);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new InvalidCredentialsException();
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        var roleNames = user.Roles
            .Select(ur => ur.Role?.Name ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var tokens = tokenIssuer.Issue(user, roleNames);

        return new LoginResult(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAt, tokens.RefreshTokenExpiresAt);
    }
}