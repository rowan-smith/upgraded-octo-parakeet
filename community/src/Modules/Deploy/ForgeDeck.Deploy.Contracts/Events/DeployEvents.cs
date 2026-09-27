using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Deploy.Contracts.Events;

public sealed record DeployReleaseEvent(
    Guid ReleaseId,
    Guid EnvironmentId,
    string ProjectKey,
    string? RequestedBy = null);

public sealed record DeployRequestedEvent(
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId,
    string ProjectKey);

public sealed record DeployQueuedEvent(
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId);

public sealed record DeployStartedEvent(
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId);

public sealed record DeploySucceededEvent(
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId);

public sealed record DeployFailedEvent(
    Guid DeploymentId,
    Guid ReleaseId,
    Guid EnvironmentId,
    string? Reason = null);

public sealed record DeployRollbackRequestedEvent(
    Guid EnvironmentId,
    Guid TargetReleaseId,
    string ProjectKey);

public sealed record DeployRolledBackEvent(
    Guid DeploymentId,
    Guid EnvironmentId,
    Guid ReleaseId);

public static class DeployEventContracts
{
    public static readonly EventContract<DeployReleaseEvent> Release =
        new("forgedeck.deploy.release", 1);
    public static readonly EventContract<DeployRequestedEvent> Requested =
        new("forgedeck.deploy.requested", 1);
    public static readonly EventContract<DeployQueuedEvent> Queued =
        new("forgedeck.deploy.queued", 1);
    public static readonly EventContract<DeployStartedEvent> Started =
        new("forgedeck.deploy.started", 1);
    public static readonly EventContract<DeploySucceededEvent> Succeeded =
        new("forgedeck.deploy.succeeded", 1);
    public static readonly EventContract<DeployFailedEvent> Failed =
        new("forgedeck.deploy.failed", 1);
    public static readonly EventContract<DeployRollbackRequestedEvent> RollbackRequested =
        new("forgedeck.deploy.rollback-requested", 1);
    public static readonly EventContract<DeployRolledBackEvent> RolledBack =
        new("forgedeck.deploy.rolled-back", 1);

    public static IEnumerable<IEventContract> All()
    {
        yield return Release;
        yield return Requested;
        yield return Queued;
        yield return Started;
        yield return Succeeded;
        yield return Failed;
        yield return RollbackRequested;
        yield return RolledBack;
    }
}
