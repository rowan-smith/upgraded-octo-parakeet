namespace ForgeDeck.Core.Licensing;

/// <summary>
/// Embedded Ed25519 public key used to verify ForgeDeck commercial licence files.
/// The matching private key is never shipped with Community builds.
/// </summary>
public static class ForgeDeckLicenceKeys
{
    public const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MCowBQYDK2VwAyEASN5mMm8ldAtVYLmRZkGjrIvxaIVVWn+x/yspm505tGU=
        -----END PUBLIC KEY-----
        """;
}
