namespace Identity.Application.Options;

public sealed class JwtIssuerOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://localhost:5001";
    public string Audience { get; set; } = "edcp";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public string SigningKeyPath { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
}