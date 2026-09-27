using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Build.Contracts.Events;

public sealed record BuildPipelineRunRequestedEvent(
    Guid DefinitionId,
    string PipelineName,
    string Branch,
    string CommitSha,
    Guid? ChangeId = null,
    string? RepositoryUrl = null,
    string? Trigger = null,
    Guid? IdempotencyKey = null);

public sealed record BuildPipelineRunQueuedEvent(
    Guid RunId,
    Guid DefinitionId,
    string PipelineName,
    string CommitSha,
    Guid? ChangeId = null);

public sealed record BuildPipelineRunStartedEvent(
    Guid RunId,
    Guid? ChangeId,
    string PipelineName,
    string CommitSha);

public sealed record BuildPipelineRunSucceededEvent(
    Guid RunId,
    Guid? ChangeId,
    string PipelineName,
    string CommitSha);

public sealed record BuildPipelineRunFailedEvent(
    Guid RunId,
    Guid? ChangeId,
    string PipelineName,
    string CommitSha,
    string? Reason = null);

public sealed record BuildPipelineRunCancelledEvent(
    Guid RunId,
    Guid? ChangeId,
    string PipelineName,
    string CommitSha);

public sealed record BuildCheckUpdatedEvent(
    Guid? ChangeId,
    Guid RunId,
    string PipelineName,
    string CommitSha,
    string Status);

public sealed record BuildArtifactProducedEvent(
    Guid RunId,
    string PipelineName,
    string ArtifactName,
    string? Uri = null);

public static class BuildEventContracts
{
    public static readonly EventContract<BuildPipelineRunRequestedEvent> PipelineRunRequested =
        new("forgedeck.build.pipeline-run-requested", 1);
    public static readonly EventContract<BuildPipelineRunQueuedEvent> PipelineRunQueued =
        new("forgedeck.build.pipeline-run-queued", 1);
    public static readonly EventContract<BuildPipelineRunStartedEvent> PipelineRunStarted =
        new("forgedeck.build.pipeline-run-started", 1);
    public static readonly EventContract<BuildPipelineRunSucceededEvent> PipelineRunSucceeded =
        new("forgedeck.build.pipeline-run-succeeded", 1);
    public static readonly EventContract<BuildPipelineRunFailedEvent> PipelineRunFailed =
        new("forgedeck.build.pipeline-run-failed", 1);
    public static readonly EventContract<BuildPipelineRunCancelledEvent> PipelineRunCancelled =
        new("forgedeck.build.pipeline-run-cancelled", 1);
    public static readonly EventContract<BuildCheckUpdatedEvent> CheckUpdated =
        new("forgedeck.build.check-updated", 1);
    public static readonly EventContract<BuildArtifactProducedEvent> ArtifactProduced =
        new("forgedeck.build.artifact-produced", 1);

    public static IEnumerable<IEventContract> All()
    {
        yield return PipelineRunRequested;
        yield return PipelineRunQueued;
        yield return PipelineRunStarted;
        yield return PipelineRunSucceeded;
        yield return PipelineRunFailed;
        yield return PipelineRunCancelled;
        yield return CheckUpdated;
        yield return ArtifactProduced;
    }
}
