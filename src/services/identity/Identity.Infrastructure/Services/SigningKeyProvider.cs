using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Services;

public sealed class SigningKeyProvider
{
    private readonly Lazy<(RsaSecurityKey Key, string Kid)> _keyAndKid;

    public SigningKeyProvider(string? pemPath = null)
    {
        _keyAndKid = new Lazy<(RsaSecurityKey, string)>(() => LoadOrGenerateKey(pemPath));
    }

    public RsaSecurityKey Key => _keyAndKid.Value.Key;
    public string KeyId => _keyAndKid.Value.Kid;

    private static (RsaSecurityKey, string) LoadOrGenerateKey(string? pemPath)
    {
        var rsa = RSA.Create(2048);
        if (!string.IsNullOrEmpty(pemPath) && File.Exists(pemPath))
        {
            rsa.ImportFromPem(File.ReadAllText(pemPath));
        }

        var kid = ComputeRfc7638Kid(rsa);
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        return (key, kid);
    }

    public static string ComputeRfc7638Kid(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA1.HashData(spki);
        return Base64UrlEncoder.Encode(hash);
    }

    public (RSAParameters PublicParameters, string Kid) ExportPublic()
    {
        var rsa = Key.Rsa ?? throw new InvalidOperationException("Signing key RSA instance is not available.");
        return (rsa.ExportParameters(false), KeyId);
    }
}