namespace Platform.Core.Capabilities;

public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    /// <summary>
    /// Path to a signed licence file. Relative paths resolve from the content root.
    /// </summary>
    public string? LicencePath { get; set; } = "data/licence.json";

    /// <summary>
    /// Optional PEM override for tests. Production uses the embedded public key.
    /// </summary>
    public string? PublicKeyPem { get; set; }
}
