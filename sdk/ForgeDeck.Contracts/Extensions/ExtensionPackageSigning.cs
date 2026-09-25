namespace ForgeDeck.Contracts.Extensions;

/// <summary>
/// Verifies ForgeDeck-signed commercial extension package envelopes.
/// Package signature answers authenticity; runtime licence answers entitlement.
/// </summary>
public interface IExtensionPackageVerifier
{
    ExtensionPackageVerificationResult Verify(ExtensionPackageEnvelope envelope);
}

public sealed record ExtensionPackageEnvelope(
    string PackageId,
    string Version,
    string ContentDigestSha256,
    string? SignatureBase64);

public sealed record ExtensionPackageVerificationResult(bool Success, string? Error);
