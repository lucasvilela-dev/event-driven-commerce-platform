using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Identity.Application.Abstractions;
using Identity.Application.Options;
using Identity.Domain.Entities;
using Identity.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Services;

internal sealed class TokenIssuer(IOptions<JwtIssuerOptions> options, SigningKeyProvider signingKey) : ITokenIssuer
{
    private readonly JwtIssuerOptions _options = options.Value;

    public TokenPair Issue(ApplicationUser user, IReadOnlyCollection<string> roles)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);

        var accessJwt = BuildJwt(user, roles, now, accessExpiresAt, "access");
        var refreshJwt = BuildJwt(user, roles, now, refreshExpiresAt, "refresh");

        return new TokenPair(accessJwt, refreshJwt)
        {
            AccessTokenExpiresAt = accessExpiresAt,
            RefreshTokenExpiresAt = refreshExpiresAt,
        };
    }

    private string BuildJwt(ApplicationUser user, IReadOnlyCollection<string> roles, DateTimeOffset now, DateTimeOffset expiresAt, string tokenType)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("token_type", tokenType),
        };
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var signing = new SigningCredentials(signingKey.Key, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: signing);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}