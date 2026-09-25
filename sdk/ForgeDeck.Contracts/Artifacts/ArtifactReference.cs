namespace ForgeDeck.Contracts.Artifacts;

/// <summary>
/// Phase-3-ready reference to a pipeline output. Not a Deploy module — only an identity contract.
/// </summary>
public sealed record ArtifactReference(
    string Kind,
    string Uri,
    string? Digest = null,
    string? MediaType = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ArtifactReference LocalFile(string path, string? mediaType = null) =>
        new("local-file", path, MediaType: mediaType);

    public static ArtifactReference OciImage(string reference, string? digest = null) =>
        new("oci-image", reference, Digest: digest, MediaType: "application/vnd.oci.image.index.v1+json");

    public static ArtifactReference ExternalRegistry(string reference, string? digest = null) =>
        new("registry-image", reference, Digest: digest);

    public static ArtifactReference Package(string reference, string? mediaType = null) =>
        new("package", reference, MediaType: mediaType);

    public static ArtifactReference DeploymentBundle(string reference) =>
        new("deployment-bundle", reference);
}
