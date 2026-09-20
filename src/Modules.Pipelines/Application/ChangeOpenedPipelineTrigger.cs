using Modules.Pipelines.Domain;
using Platform.Contracts.Events;

namespace Modules.Pipelines.Application;

public sealed class ChangeOpenedPipelineTrigger(PipelineService pipelines, IPipelineRepository repository) : IEventHandler<ChangeOpened>
{
    public async Task HandleAsync(ChangeOpened domainEvent, CancellationToken cancellationToken = default)
    {
        foreach (var definition in repository.ListDefinitions().Where(definition => definition.Triggers.Contains(PipelineTrigger.ChangeOpened)))
            await pipelines.RunAsync(definition.Id, PipelineTrigger.ChangeOpened, domainEvent.SourceBranch, domainEvent.ChangeId, domainEvent.CommitSha, cancellationToken);
    }
}
