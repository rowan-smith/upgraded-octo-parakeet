using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Persistence;
using ForgeDeck.Messaging.Serialization;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Messaging.Diagnostics;

public sealed class EventDiagnosticsService(
    IEventOutbox outbox,
    IEventInbox inbox,
    EventSubscriber subscriber,
    IEventSerializer serializer,
    IEventBus bus,
    IDbContextFactory<MessagingDbContext> dbFactory) : IEventDiagnostics
{
    public Task<IReadOnlyList<OutboxRecord>> ListRecentOutboxAsync(int take = 50, CancellationToken cancellationToken = default) =>
        outbox.ListRecentAsync(take, cancellationToken);

    public Task<IReadOnlyList<DeliveryRecord>> ListFailedDeliveriesAsync(int take = 50, CancellationToken cancellationToken = default) =>
        inbox.ListFailedAsync(take, cancellationToken);

    public Task<IReadOnlyList<EventSubscriptionInfo>> ListSubscriptionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(subscriber.ListSubscriptions());

    public Task<IReadOnlyList<OutboxRecord>> ListOutboxBacklogAsync(CancellationToken cancellationToken = default) =>
        outbox.ListBacklogAsync(cancellationToken);

    public async Task<CorrelationTrace?> GetCorrelationTraceAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var needle = correlationId.ToString();
        var envelopes = await db.Outbox
            .Where(o => o.EnvelopeJson.Contains(needle))
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.EnvelopeJson)
            .ToListAsync(cancellationToken);

        var nodes = new List<CorrelationTraceNode>();
        foreach (var json in envelopes)
        {
            var envelope = serializer.DeserializeEnvelope(json);
            if (envelope.CorrelationId != correlationId)
            {
                continue;
            }

            nodes.Add(new CorrelationTraceNode(
                envelope.Id,
                envelope.Type,
                envelope.Version,
                envelope.OccurredAt,
                envelope.CausationId,
                envelope.Publisher));
        }

        return nodes.Count == 0 ? null : new CorrelationTrace(correlationId, nodes);
    }

    public async Task RetryDeliveryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await inbox.ResetForRetryAsync(eventId, consumerId, cancellationToken);
        var record = await outbox.GetAsync(eventId, cancellationToken)
                     ?? throw new InvalidOperationException($"Outbox event {eventId} not found.");
        var envelope = serializer.DeserializeEnvelope(record.EnvelopeJson);
        await bus.DispatchAsync(envelope, cancellationToken);
    }

    public Task AcknowledgeDeliveryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default) =>
        inbox.AcknowledgeAsync(eventId, consumerId, cancellationToken);
}
