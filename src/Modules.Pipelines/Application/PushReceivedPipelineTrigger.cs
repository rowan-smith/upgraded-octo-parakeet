using Modules.Pipelines.Domain;
using Platform.Contracts.Events;

namespace Modules.Pipelines.Application;

public sealed class PushReceivedPipelineTrigger(PipelineService pipelines, IPipelineRepository repository) : IEventHandler<PushReceived>
{
    public async Task HandleAsync(PushReceived domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var definition in repository.ListDefinitions().Where(definition => definition.Triggers.Contains(PipelineTrigger.Push)))
            await pipelines.RunAsync(definition.Id, PipelineTrigger.Push, domainEvent.Branch, commitSha: domainEvent.CommitSha, token: cancellationToken);
    }
}
