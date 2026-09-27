using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Review.Contracts.Events;

namespace ForgeDeck.Build.Application;

/// <summary>Maps review revision updates to pipeline run requests for ChangeUpdated triggers.</summary>
public sealed class ReviewRevisionUpdatedPipelineTrigger(IPipelineStore store, IEventPublisher events)
    : IEventHandler<ReviewRevisionUpdatedEvent>
{
    public string ConsumerId => "forgedeck.build.review-revision-updated";

    public async Task HandleAsync(EventEnvelope<ReviewRevisionUpdatedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var data = envelope.Data;
        foreach (var definition in store.ListDefinitions()
                     .Where(d => d.Enabled && d.Triggers.Contains(PipelineTrigger.ChangeUpdated)))
        {
            await events.PublishAsync(
                new BuildPipelineRunRequestedEvent(
                    definition.Id,
                    definition.Name,
                    data.SourceBranch,
                    data.CommitSha,
                    data.ChangeId,
                    data.RepositoryUrl,
                    nameof(PipelineTrigger.ChangeUpdated),
                    EventIdempotency.Combine(envelope.Id, definition.Id)),
                BuildEventPublishOptions.Default,
                cancellationToken);
        }
    }
}
