using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Services;

public sealed class SigningKeyProvider
{
    private readonly Lazy<RsaSecurityKey> _key;

    public SigningKeyProvider(string keyId, string? pemPath = null)
    {
        _key = new Lazy<RsaSecurityKey>(() => LoadOrGenerateKey(keyId, pemPath));
        KeyId = keyId;
    }

    public RsaSecurityKey Key => _key.Value;

    public string KeyId { get; }

    private static RsaSecurityKey LoadOrGenerateKey(string keyId, string? pemPath)
    {
        RSA rsa = RSA.Create(2048);
        if (!string.IsNullOrEmpty(pemPath) && File.Exists(pemPath))
        {
            rsa.ImportFromPem(File.ReadAllText(pemPath));
        }
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }
}