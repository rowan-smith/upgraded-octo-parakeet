using ForgeDeck.Deploy.Domain;

namespace ForgeDeck.Deploy.Application;

/// <summary>
/// Executes a queued deployment. Phase H seam — swap for agent/orchestrator implementations later.
/// </summary>
public interface IDeploymentExecutor
{
    Task ExecuteAsync(Deployment deployment, Guid releaseId, CancellationToken cancellationToken = default);
}
