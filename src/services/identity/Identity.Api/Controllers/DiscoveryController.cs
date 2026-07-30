using Identity.Application.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Identity.Api.Controllers;

[ApiController]
[Route(".well-known")]
public sealed class DiscoveryController(IOptions<JwtIssuerOptions> options) : ControllerBase
{
    private readonly JwtIssuerOptions _options = options.Value;

    [HttpGet("openid-configuration")]
    [Produces("application/json")]
    public IActionResult Get()
    {
        var issuer = _options.Issuer.TrimEnd('/');
        var doc = new
        {
            issuer,
            jwks_uri = $"{issuer}/.well-known/jwks",
            token_endpoint = $"{issuer}/api/identity/login",
            response_types_supported = Array.Empty<string>(),
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            token_endpoint_auth_methods_supported = new[] { "none" },
        };
        return Ok(doc);
    }
}