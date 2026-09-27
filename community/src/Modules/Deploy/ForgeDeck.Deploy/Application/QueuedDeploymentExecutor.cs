using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

/// <summary>
/// Agent-backed executor: leaves the deployment Pending after queue acknowledgement.
/// A registered deploy agent claims and completes the work.
/// </summary>
public sealed class QueuedDeploymentExecutor : IDeploymentExecutor
{
    public Task ExecuteAsync(Deployment deployment, Guid releaseId, CancellationToken cancellationToken = default)
    {
        // DeployService already saved Pending + published DeployQueuedEvent.
        // Agents pick up via ClaimWork; do not transition status here.
        _ = deployment;
        _ = releaseId;
        return Task.CompletedTask;
    }
}
