using Platform.Contracts.Artifacts;

namespace Modules.Pipelines.Domain;

public sealed class RunArtifact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid JobId { get; init; }
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public required long Size { get; init; }
    public required string StoragePath { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Phase-3-ready output identity (local file today; OCI/registry later).</summary>
    public ArtifactReference ToReference() => ArtifactReference.LocalFile(StoragePath, ContentType);
}
