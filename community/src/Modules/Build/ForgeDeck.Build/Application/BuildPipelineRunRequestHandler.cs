using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Build.Application;

/// <summary>
/// Creates a pipeline run once per IdempotencyKey (or EventId when key is absent).
/// </summary>
public sealed class BuildPipelineRunRequestHandler(PipelineService pipelines, IPipelineStore store)
    : IEventHandler<BuildPipelineRunRequestedEvent>
{
    public string ConsumerId => "forgedeck.build.pipeline-run-requested";

    public Task HandleAsync(EventEnvelope<BuildPipelineRunRequestedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        var key = data.IdempotencyKey ?? envelope.Id;
        if (store.FindRunByIdempotencyKey(key) is not null)
        {
            return Task.CompletedTask;
        }

        var trigger = ParseTrigger(data.Trigger);
        pipelines.StartRun(
            data.DefinitionId,
            trigger,
            data.Branch,
            data.CommitSha,
            data.ChangeId,
            data.RepositoryUrl,
            key);
        return Task.CompletedTask;
    }

    private static PipelineTrigger ParseTrigger(string? trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return PipelineTrigger.Manual;
        }

        return Enum.TryParse<PipelineTrigger>(trigger, ignoreCase: true, out var parsed)
            ? parsed
            : PipelineTrigger.Manual;
    }
}
