using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Build.Application;

public sealed class ChangeOpenedPipelineTrigger(PipelineService pipelines, IPipelineStore store) : IEventHandler<ChangeOpened>
{
    public Task HandleAsync(ChangeOpened domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var definition in store.ListDefinitions().Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.ChangeOpened)))
        {
            pipelines.StartRun(definition.Id, PipelineTrigger.ChangeOpened, domainEvent.SourceBranch, domainEvent.CommitSha, domainEvent.ChangeId, domainEvent.RepositoryUrl);
        }

        return Task.CompletedTask;
    }
}
