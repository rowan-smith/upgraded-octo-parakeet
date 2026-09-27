namespace ForgeDeck.Deploy.Domain;

public sealed class DeploymentEnvironment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum DeploymentStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    RolledBack
}

public sealed class Deployment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Version { get; set; } = "";
    public string? Notes { get; set; }
    public string TriggeredBy { get; set; } = "";
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Succeeded;
    public Guid? RollbackOfId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record DeployBuildRunReference(
    Guid RunId,
    string PipelineName,
    string CommitSha,
    Guid? ChangeId,
    DateTimeOffset RecordedAt);

public sealed record DeployArtifactReference(
    Guid RunId,
    string PipelineName,
    string ArtifactName,
    string? Uri,
    DateTimeOffset RecordedAt);
