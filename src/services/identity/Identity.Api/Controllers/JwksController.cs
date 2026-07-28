using System.Security.Cryptography;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Controllers;

[ApiController]
[Route(".well-known")]
public sealed class JwksController(SigningKeyProvider signingKey) : ControllerBase
{
    [HttpGet("jwks")]
    [Produces("application/json")]
    public IActionResult Get()
    {
        var (publicParams, kid) = signingKey.ExportPublic();
        var jwk = new
        {
            kty = "RSA",
            use = "sig",
            alg = "RS256",
            kid,
            n = Base64UrlEncoder.Encode(publicParams.Modulus),
            e = Base64UrlEncoder.Encode(publicParams.Exponent),
        };
        return Ok(new { keys = new[] { jwk } });
    }
}