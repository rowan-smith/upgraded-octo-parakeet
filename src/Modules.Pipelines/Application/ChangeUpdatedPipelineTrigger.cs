using Modules.Pipelines.Domain;
using Platform.Contracts.Events;

namespace Modules.Pipelines.Application;

public sealed class ChangeUpdatedPipelineTrigger(PipelineService pipelines, IPipelineStore store) : IEventHandler<ChangeUpdated>
{
    public Task HandleAsync(ChangeUpdated domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var definition in store.ListDefinitions().Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.ChangeUpdated)))
            pipelines.StartRun(definition.Id, PipelineTrigger.ChangeUpdated, domainEvent.SourceBranch, domainEvent.CommitSha, domainEvent.ChangeId, domainEvent.RepositoryUrl);
        return Task.CompletedTask;
    }
}
