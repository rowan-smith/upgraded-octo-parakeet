using ForgeDeck.Contracts.Events;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

/// <summary>
/// Default executor: immediately transitions Pending → InProgress (Started) → Succeeded
/// and publishes the corresponding deploy events. Used when Deploy:ExecutionMode is Immediate.
/// </summary>
public sealed class ImmediateDeploymentExecutor(
    IDeployStore store,
    IEventPublisher events) : IDeploymentExecutor
{
    public Task ExecuteAsync(Deployment deployment, Guid releaseId, CancellationToken cancellationToken = default)
    {
        deployment.Status = DeploymentStatus.InProgress;
        store.SaveDeployment(deployment);
        _ = events.PublishAsync(
            new DeployStartedEvent(deployment.Id, releaseId, deployment.EnvironmentId),
            DeployEventPublishOptions.Default,
            cancellationToken);

        deployment.Status = DeploymentStatus.Succeeded;
        store.SaveDeployment(deployment);
        _ = events.PublishAsync(
            new DeploySucceededEvent(deployment.Id, releaseId, deployment.EnvironmentId),
            DeployEventPublishOptions.Default,
            cancellationToken);

        return Task.CompletedTask;
    }
}
