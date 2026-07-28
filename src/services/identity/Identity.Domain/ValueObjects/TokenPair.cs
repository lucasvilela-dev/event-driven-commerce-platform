namespace Identity.Domain.ValueObjects;

public readonly record struct TokenPair(string AccessToken, string RefreshToken)
{
    public DateTimeOffset AccessTokenExpiresAt { get; init; }
    public DateTimeOffset RefreshTokenExpiresAt { get; init; }
}