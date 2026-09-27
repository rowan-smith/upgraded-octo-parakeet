using ForgeDeck.Contracts.Events;
using ForgeDeck.Deploy.Contracts.Events;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployRollbackRequestHandler(DeployService deploy, IDeployStore store)
    : IEventHandler<DeployRollbackRequestedEvent>
{
    public string ConsumerId => "forgedeck.deploy.rollback-requested";

    public Task HandleAsync(EventEnvelope<DeployRollbackRequestedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        if (store.FindEnvironment(data.EnvironmentId) is null)
        {
            return Task.CompletedTask;
        }

        deploy.RollbackToRelease(data.EnvironmentId, data.TargetReleaseId, "system", data.ProjectKey);
        return Task.CompletedTask;
    }
}
