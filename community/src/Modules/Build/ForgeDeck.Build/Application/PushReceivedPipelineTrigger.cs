using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Build.Application;

public sealed class PushReceivedPipelineTrigger(PipelineService pipelines, IPipelineStore store) : IEventHandler<PushReceived>
{
    public Task HandleAsync(PushReceived domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var definition in store.ListDefinitions().Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.Push)))
        {
            pipelines.StartRun(definition.Id, PipelineTrigger.Push, domainEvent.Branch, domainEvent.CommitSha);
        }

        return Task.CompletedTask;
    }
}
