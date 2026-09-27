using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;

namespace ForgeDeck.Build.Application;

/// <summary>Maps git repository pushes to pipeline run requests for Push triggers.</summary>
public sealed class GitPushPipelineTrigger(IPipelineStore store, IEventPublisher events)
    : IEventHandler<GitRepositoryPushEvent>
{
    public string ConsumerId => "forgedeck.build.git-push";

    public async Task HandleAsync(EventEnvelope<GitRepositoryPushEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        foreach (var definition in store.ListDefinitions()
                     .Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.Push)))
        {
            await events.PublishAsync(
                new BuildPipelineRunRequestedEvent(
                    definition.Id,
                    definition.Name,
                    data.Branch,
                    data.CommitSha,
                    ChangeId: null,
                    RepositoryUrl: null,
                    Trigger: nameof(PipelineTrigger.Push),
                    IdempotencyKey: EventIdempotency.Combine(envelope.Id, definition.Id)),
                BuildEventPublishOptions.Default,
                cancellationToken);
        }
    }
}
