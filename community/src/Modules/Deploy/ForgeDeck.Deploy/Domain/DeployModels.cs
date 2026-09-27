using System.Text.Json.Serialization;

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

public enum DeploymentAgentStatus
{
    Offline,
    Online,
    Busy
}

public sealed class DeploymentAgent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string TokenHash { get; init; }
    [JsonInclude] public DeploymentAgentStatus Status { get; private set; } = DeploymentAgentStatus.Offline;
    [JsonInclude] public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public IReadOnlyList<string> Labels { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public void Heartbeat(DeploymentAgentStatus status, IReadOnlyList<string>? labels = null)
    {
        Status = status;
        if (labels is not null)
        {
            Labels = labels;
        }

        LastHeartbeatAt = DateTimeOffset.UtcNow;
    }

    public void MarkOffline() => Status = DeploymentAgentStatus.Offline;

    public bool IsHealthy(TimeSpan? window = null)
    {
        var limit = window ?? TimeSpan.FromMinutes(2);
        return LastHeartbeatAt is { } at && DateTimeOffset.UtcNow - at < limit;
    }
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
    public Guid? AgentId { get; set; }
    public string? AgentLog { get; set; }
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
