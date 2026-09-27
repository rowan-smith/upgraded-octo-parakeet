using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Deploy.Application;

public sealed class PipelineSucceededDeployHandler(IDeployStore store) : IEventHandler<BuildPipelineRunSucceededEvent>
{
    public string ConsumerId => "forgedeck.deploy.pipeline-succeeded";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunSucceededEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        store.SaveBuildRunReference(e.RunId, e.PipelineName, e.CommitSha, e.ChangeId);
        return Task.CompletedTask;
    }
}

public sealed class BuildArtifactAvailableHandler(IDeployStore store) : IEventHandler<BuildArtifactProducedEvent>
{
    public string ConsumerId => "forgedeck.deploy.artifact-produced";

    public Task HandleAsync(EventEnvelope<BuildArtifactProducedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        store.SaveArtifactReference(e.RunId, e.PipelineName, e.ArtifactName, e.Uri);
        return Task.CompletedTask;
    }
}
