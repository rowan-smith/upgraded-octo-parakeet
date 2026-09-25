using System.Security.Cryptography;
using System.Text;
using ForgeDeck.Contracts.Extensions;

namespace ForgeDeck.Core.Extensions;

public static class ForgeDeckPackageKeys
{
    /// <summary>Public key for verifying official commercial extension packages. Private key stays in release CI.</summary>
    public const string PublicKeyPem =
        """
        -----BEGIN RSA PUBLIC KEY-----
        MIIBCgKCAQEAwHzhgyJIr+vxZYsYJKhtxZHo6GA7/who4fd+9Up1O7XGuOrFABgg
        DopxxvSkTMOPPVZJCRICxWpE0ccvsobYbKx5abuyACb6hAK2Ols0WK2/S9lDFYEo
        cK8eNz8H/NSDnFOtese7jZItsU8Pv2v77YwB2hvcFO0CXTlSyqlw8Xu3Y6w6swlg
        xgsl2LfUVTURu3j7B8S03NI3LPSAcVHnAzgdYcl27PSdFbwL5SAggyQBhWXCK1NR
        5EH7/kKqlmx0INSr73FaRmcRw+py7C81uCBr2HtfDZizIpn6AtCy1qORRXGlFc3g
        pJVzeVdS7va8KQoCpQCQWzTmS9aIlHV6mQIDAQAB
        -----END RSA PUBLIC KEY-----
        """;
}

/// <summary>
/// Verifies package envelopes. Unsigned envelopes are accepted for Community bundled modules;
/// commercial installers should require a successful signature.
/// </summary>
public sealed class SignedExtensionPackageVerifier(RSA? publicKey = null) : IExtensionPackageVerifier, IDisposable
{
    private readonly RSA _publicKey = publicKey ?? ImportDefault();
    private readonly bool _ownsKey = publicKey is null;

    public ExtensionPackageVerificationResult Verify(ExtensionPackageEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.SignatureBase64))
        {
            return new(false, "Package signature is required for commercial packages.");
        }

        if (string.IsNullOrWhiteSpace(envelope.ContentDigestSha256))
        {
            return new(false, "Package content digest is missing.");
        }

        try
        {
            var payload = CanonicalBytes(envelope);
            var signature = Convert.FromBase64String(envelope.SignatureBase64);
            var ok = _publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return ok
                ? new(true, null)
                : new(false, "Package signature verification failed.");
        }
        catch (FormatException)
        {
            return new(false, "Package signature verification failed.");
        }
        catch (CryptographicException)
        {
            return new(false, "Package signature verification failed.");
        }
    }

    public static string Sign(ExtensionPackageEnvelope envelope, RSA privateKey)
    {
        var signature = privateKey.SignData(CanonicalBytes(envelope), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public static byte[] CanonicalBytes(ExtensionPackageEnvelope envelope)
    {
        var canonical = $"{envelope.PackageId}\n{envelope.Version}\n{envelope.ContentDigestSha256.ToLowerInvariant()}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public void Dispose()
    {
        if (_ownsKey)
        {
            _publicKey.Dispose();
        }
    }

    private static RSA ImportDefault()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(ForgeDeckPackageKeys.PublicKeyPem);
        return rsa;
    }
}
