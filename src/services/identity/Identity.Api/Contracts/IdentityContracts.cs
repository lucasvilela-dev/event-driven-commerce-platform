namespace Identity.Api.Contracts;

public sealed record RegisterRequest(string Email, string Password);

public sealed record RegisterResponse(Guid UserId);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);