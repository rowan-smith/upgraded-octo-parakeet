using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Contracts.Licensing;

namespace Platform.Core.Licensing;

public static class LicenceCryptography
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] CanonicalPayload(LicenceDocument document)
    {
        var payload = new
        {
            organisationId = document.OrganisationId,
            issuedAt = document.IssuedAt.ToUniversalTime(),
            modules = document.Modules
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => new
                    {
                        edition = pair.Value.Edition,
                        capabilities = pair.Value.Capabilities
                            .OrderBy(capability => capability, StringComparer.Ordinal)
                            .ToArray(),
                        expiresAt = pair.Value.ExpiresAt?.ToUniversalTime()
                    },
                    StringComparer.Ordinal)
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public static string Sign(LicenceDocument document, RSA privateKey)
    {
        var signature = privateKey.SignData(CanonicalPayload(document), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public static bool Verify(LicenceDocument document, RSA publicKey)
    {
        if (string.IsNullOrWhiteSpace(document.Signature)) return false;
        try
        {
            var signature = Convert.FromBase64String(document.Signature);
            return publicKey.VerifyData(CanonicalPayload(document), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static RSA ImportPublicKeyPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    public static RSA ImportPrivateKeyPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
