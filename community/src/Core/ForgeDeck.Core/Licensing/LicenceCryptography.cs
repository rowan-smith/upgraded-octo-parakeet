using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeDeck.Contracts.Licensing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace ForgeDeck.Core.Licensing;

/// <summary>
/// Ed25519 licence signing/verification. The application embeds only the public key.
/// </summary>
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
            version = document.Version <= 0 ? 1 : document.Version,
            licenceId = document.LicenceId,
            customer = document.Customer,
            organisationId = document.OrganisationId,
            issuedAt = document.IssuedAt.ToUniversalTime(),
            validUntil = document.ValidUntil?.ToUniversalTime(),
            instanceId = document.InstanceId,
            capacity = document.Capacity is null ? null : new { users = document.Capacity.Users },
            maintenance = document.Maintenance is null ? null : new
            {
                until = document.Maintenance.Until?.ToUniversalTime(),
                securityUntil = document.Maintenance.SecurityUntil?.ToUniversalTime(),
                supportUntil = document.Maintenance.SupportUntil?.ToUniversalTime()
            },
            productVersion = document.ProductVersion is null ? null : new { maxMajor = document.ProductVersion.MaxMajor },
            features = document.Features is null
                ? null
                : document.Features.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
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

    public static LicenceKeyPair CreateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return new LicenceKeyPair(
            (Ed25519PrivateKeyParameters)pair.Private,
            (Ed25519PublicKeyParameters)pair.Public);
    }

    public static string Sign(LicenceDocument document, LicenceKeyPair keyPair) =>
        Sign(document, keyPair.Private);

    public static string Sign(LicenceDocument document, Ed25519PrivateKeyParameters privateKey)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var payload = CanonicalPayload(document);
        signer.BlockUpdate(payload, 0, payload.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool Verify(LicenceDocument document, LicenceKeyPair keyPair) =>
        Verify(document, keyPair.Public);

    public static bool Verify(LicenceDocument document, Ed25519PublicKeyParameters publicKey)
    {
        if (string.IsNullOrWhiteSpace(document.Signature))
        {
            return false;
        }

        try
        {
            var signature = Convert.FromBase64String(document.Signature);
            var verifier = new Ed25519Signer();
            verifier.Init(false, publicKey);
            var payload = CanonicalPayload(document);
            verifier.BlockUpdate(payload, 0, payload.Length);
            return verifier.VerifySignature(signature);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string ExportPublicKeyPem(Ed25519PublicKeyParameters publicKey)
    {
        using var writer = new StringWriter();
        new PemWriter(writer).WriteObject(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey));
        return writer.ToString();
    }

    public static string ExportPrivateKeyPem(Ed25519PrivateKeyParameters privateKey)
    {
        using var writer = new StringWriter();
        new PemWriter(writer).WriteObject(PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey));
        return writer.ToString();
    }

    public static Ed25519PublicKeyParameters ImportPublicKeyPem(string pem)
    {
        var key = ImportPem(pem);
        return key switch
        {
            Ed25519PublicKeyParameters pub => pub,
            Ed25519PrivateKeyParameters priv => priv.GeneratePublicKey(),
            _ => throw new CryptographicException("PEM did not contain an Ed25519 public key.")
        };
    }

    public static Ed25519PrivateKeyParameters ImportPrivateKeyPem(string pem)
    {
        var key = ImportPem(pem);
        return key as Ed25519PrivateKeyParameters
               ?? throw new CryptographicException("PEM did not contain an Ed25519 private key.");
    }

    private static AsymmetricKeyParameter ImportPem(string pem)
    {
        using var reader = new StringReader(pem);
        var obj = new PemReader(reader).ReadObject();
        return obj switch
        {
            AsymmetricCipherKeyPair pair => pair.Private,
            AsymmetricKeyParameter key => key,
            _ => throw new CryptographicException("Unsupported PEM object for licence keys.")
        };
    }
}

/// <summary>Ephemeral or loaded Ed25519 key pair for signing licences (tests / LicenceSigner).</summary>
public sealed class LicenceKeyPair(Ed25519PrivateKeyParameters privateKey, Ed25519PublicKeyParameters publicKey)
{
    public Ed25519PrivateKeyParameters Private { get; } = privateKey;
    public Ed25519PublicKeyParameters Public { get; } = publicKey;
    public string PublicKeyPem => LicenceCryptography.ExportPublicKeyPem(Public);
    public string PrivateKeyPem => LicenceCryptography.ExportPrivateKeyPem(Private);

    public static LicenceKeyPair FromPrivatePem(string privateKeyPem)
    {
        var privateKey = LicenceCryptography.ImportPrivateKeyPem(privateKeyPem);
        return new LicenceKeyPair(privateKey, privateKey.GeneratePublicKey());
    }
}
