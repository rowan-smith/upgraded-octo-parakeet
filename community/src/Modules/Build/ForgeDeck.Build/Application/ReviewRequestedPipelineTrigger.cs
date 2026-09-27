using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Review.Contracts.Events;

namespace ForgeDeck.Build.Application;

/// <summary>Maps review-requested to pipeline run requests for ChangeOpened triggers.</summary>
public sealed class ReviewRequestedPipelineTrigger(IPipelineStore store, IEventPublisher events)
    : IEventHandler<ReviewRequestedEvent>
{
    public string ConsumerId => "forgedeck.build.review-requested";

    public async Task HandleAsync(EventEnvelope<ReviewRequestedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        foreach (var definition in store.ListDefinitions()
                     .Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.ChangeOpened)))
        {
            await events.PublishAsync(
                new BuildPipelineRunRequestedEvent(
                    definition.Id,
                    definition.Name,
                    data.SourceBranch,
                    data.CommitSha,
                    data.ChangeId,
                    data.RepositoryUrl,
                    nameof(PipelineTrigger.ChangeOpened),
                    EventIdempotency.Combine(envelope.Id, definition.Id)),
                BuildEventPublishOptions.Default,
                cancellationToken);
        }
    }
}
