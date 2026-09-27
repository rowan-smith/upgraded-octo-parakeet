using ForgeDeck.Contracts.Events;
using ForgeDeck.Deploy.Contracts.Events;

namespace ForgeDeck.Deploy.Application;

public sealed class DeployReleaseHandler(DeployService deploy, IDeployStore store)
    : IEventHandler<DeployReleaseEvent>
{
    public string ConsumerId => "forgedeck.deploy.release";

    public Task HandleAsync(EventEnvelope<DeployReleaseEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        var environment = store.FindEnvironment(data.EnvironmentId);
        if (environment is null)
        {
            return Task.CompletedTask;
        }

        deploy.CreateDeployment(
            environment.ProjectId,
            data.EnvironmentId,
            data.ReleaseId.ToString("N"),
            data.RequestedBy ?? "system",
            notes: null,
            releaseId: data.ReleaseId);
        return Task.CompletedTask;
    }
}
