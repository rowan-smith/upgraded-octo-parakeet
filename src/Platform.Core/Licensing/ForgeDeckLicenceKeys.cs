namespace Platform.Core.Licensing;

/// <summary>
/// Embedded RSA public key used to verify ForgeDeck commercial licence files.
/// The matching private key is never shipped with Community builds.
/// </summary>
public static class ForgeDeckLicenceKeys
{
    public const string PublicKeyPem =
        """
        -----BEGIN RSA PUBLIC KEY-----
        MIIBCgKCAQEAyEMEd5qZ5WVl4/p4cb9e8mNB/88VrNSVyvChFpgL3Vb1rVQs/qmw
        tgxvskGCe62MP/qKUrb4ZeeZKH5B/npYq/JWUeujeGDp9Bx7DrsTg96RkZEPM0O6
        pSICNpmaQc8mmPe8i9VRWvHDpAcvIn6SzQaQcJUQrWD9WvxbZSP3T4msw5+ilydn
        CHqgy7OOCfQel8oclwlHfsObi4CrWJJ7Tz39BvYXEYEQckcSoZsCuxa14y2F+b9E
        EpZNQuU5fvCUPB0WuhHj0vleYSKTmpCZoVon0Q+atCyn0EeQxe8gtph5GtGehLCO
        +nOy0nrTOa9vIFC41LAt1BblG6+n8rrXcQIDAQAB
        -----END RSA PUBLIC KEY-----
        """;
}
