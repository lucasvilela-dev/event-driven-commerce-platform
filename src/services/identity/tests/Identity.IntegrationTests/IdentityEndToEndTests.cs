using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace Identity.IntegrationTests;

public class IdentityEndToEndTests : IClassFixture<IdentityWebFactory>
{
    private readonly IdentityWebFactory _factory;
    private readonly HttpClient _client;

    public IdentityEndToEndTests(IdentityWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.MigrateDatabaseAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Well_known_discovery_returns_minimal_document()
    {
        var response = await _client.GetAsync("/.well-known/openid-configuration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        doc.Should().NotBeNull();
        doc!.Should().ContainKey("issuer");
        doc.Should().ContainKey("jwks_uri");
        doc["jwks_uri"].ToString()!.Should().EndWith("/.well-known/jwks");
    }

    [Fact]
    public async Task Jwks_endpoint_returns_single_rsa_public_key()
    {
        var response = await _client.GetAsync("/.well-known/jwks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JwksDocument>();
        doc.Should().NotBeNull();
        doc!.Keys.Should().HaveCount(1);
        var jwk = doc.Keys[0];
        jwk.Kty.Should().Be("RSA");
        jwk.Use.Should().Be("sig");
        jwk.Alg.Should().Be("RS256");
        jwk.Kid.Should().NotBeNullOrEmpty();
        jwk.N.Should().NotBeNullOrEmpty();
        jwk.E.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_then_login_round_trip_yields_validatable_jwt_pair()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var password = "P@ssw0rd!";

        var registerResponse = await _client.PostAsJsonAsync("/api/identity/register", new { Email = email, Password = password });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        registerBody!["userId"].ToString().Should().NotBeNullOrEmpty();

        var duplicateResponse = await _client.PostAsJsonAsync("/api/identity/register", new { Email = email, Password = password });
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var loginResponse = await _client.PostAsJsonAsync("/api/identity/login", new { Email = email, Password = password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        loginBody.Should().NotBeNull();
        loginBody!.AccessToken.Should().NotBeNullOrEmpty();
        loginBody.RefreshToken.Should().NotBeNullOrEmpty();
        loginBody.AccessToken.Should().NotBe(loginBody.RefreshToken);

        await AssertJwtValidatesAgainstJwksAsync(loginBody.AccessToken, email, expectedRole: "customer");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var email = $"wrong-{Guid.NewGuid():N}@example.com";

        var registerResponse = await _client.PostAsJsonAsync("/api/identity/register", new { Email = email, Password = "RightPassword1!" });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var badLogin = await _client.PostAsJsonAsync("/api/identity/login", new { Email = email, Password = "WrongPassword1!" });
        badLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_unknown_user_returns_401()
    {
        var unknown = await _client.PostAsJsonAsync("/api/identity/login", new { Email = "ghost@example.com", Password = "any" });
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task AssertJwtValidatesAgainstJwksAsync(string jwt, string expectedEmail, string expectedRole)
    {
        var jwksResponse = await _client.GetAsync("/.well-known/jwks");
        var jwksJson = await jwksResponse.Content.ReadAsStringAsync();
        var keys = new JsonWebKeySet(jwksJson);

        var discoveryResponse = await _client.GetAsync("/.well-known/openid-configuration");
        var discovery = await discoveryResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var issuer = discovery!["issuer"].ToString();

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = "edcp",
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            IssuerSigningKeys = keys.Keys,
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(jwt, validationParameters, out var validatedToken);

        var emailClaim = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;
        emailClaim.Should().Be(expectedEmail);
        var roleClaims = principal.FindAll("role").Select(c => c.Value).ToList();
        roleClaims.Should().Contain(expectedRole);
        validatedToken.Should().NotBeNull();
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);

    private sealed record JwksDocument(List<JwkDto> Keys);

    private sealed record JwkDto(string Kty, string Use, string Alg, string Kid, string N, string E);
}