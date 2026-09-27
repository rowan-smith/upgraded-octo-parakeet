using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Messaging.Inbox;

public interface IEventInbox
{
    Task<bool> TryBeginAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid eventId, string consumerId, string error, int attempt, DateTimeOffset? nextAttemptAt, bool deadLetter, CancellationToken cancellationToken = default);
    Task CancelConsumerAsync(string consumerId, CancellationToken cancellationToken = default);
    Task ResetForRetryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryRecord>> ListFailedAsync(int take, CancellationToken cancellationToken = default);
    Task<DeliveryRecord?> GetAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default);
    Task EnsureDeliveryRowAsync(EventEnvelope envelope, string consumerId, CancellationToken cancellationToken = default);
}

public sealed class EfEventInbox(IDbContextFactory<MessagingDbContext> factory) : IEventInbox
{
    public async Task EnsureDeliveryRowAsync(EventEnvelope envelope, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var exists = await db.Deliveries.AnyAsync(
            d => d.EventId == envelope.Id && d.ConsumerId == consumerId,
            cancellationToken);
        if (exists)
        {
            return;
        }

        db.Deliveries.Add(new DeliveryEntity
        {
            EventId = envelope.Id,
            ConsumerId = consumerId,
            EventType = envelope.Type,
            Version = envelope.Version,
            State = DeliveryState.Pending,
            Attempts = 0,
            CorrelationId = envelope.CorrelationId,
            CreatedAt = envelope.OccurredAt.UtcDateTime,
            EnvelopeJson = "{}"
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryBeginAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId && d.ConsumerId == consumerId,
            cancellationToken);

        if (existing is not null
            && (existing.State is DeliveryState.Processed or DeliveryState.Cancelled
                || string.Equals(existing.LastError, "Acknowledged", StringComparison.Ordinal)))
        {
            return false;
        }

        var inboxHit = await db.Inbox.AnyAsync(
            i => i.EventId == eventId && i.ConsumerId == consumerId,
            cancellationToken);
        if (inboxHit)
        {
            return false;
        }

        if (existing is null
            || existing.State is not (DeliveryState.Pending or DeliveryState.Failed))
        {
            return false;
        }

        existing.State = DeliveryState.Processing;
        existing.LastAttemptAt = DateTimeOffset.UtcNow;
        existing.Attempts++;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkProcessedAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var inboxExists = await db.Inbox.AnyAsync(
            i => i.EventId == eventId && i.ConsumerId == consumerId,
            cancellationToken);
        if (!inboxExists)
        {
            db.Inbox.Add(new InboxEntity
            {
                EventId = eventId,
                ConsumerId = consumerId,
                ProcessedAt = DateTimeOffset.UtcNow
            });
        }

        var delivery = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId && d.ConsumerId == consumerId,
            cancellationToken);
        if (delivery is not null)
        {
            delivery.State = DeliveryState.Processed;
            delivery.LastError = null;
            delivery.NextAttemptAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid eventId,
        string consumerId,
        string error,
        int attempt,
        DateTimeOffset? nextAttemptAt,
        bool deadLetter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var delivery = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId && d.ConsumerId == consumerId,
            cancellationToken);
        if (delivery is null)
        {
            return;
        }

        delivery.State = deadLetter ? DeliveryState.DeadLetter : DeliveryState.Failed;
        delivery.LastError = Truncate(error);
        delivery.Attempts = attempt;
        delivery.LastAttemptAt = DateTimeOffset.UtcNow;
        delivery.NextAttemptAt = nextAttemptAt?.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelConsumerAsync(string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Deliveries
            .Where(d => d.ConsumerId == consumerId
                        && (d.State == DeliveryState.Pending
                            || d.State == DeliveryState.Failed
                            || d.State == DeliveryState.Processing))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.State = DeliveryState.Cancelled;
            row.LastError = "Consumer removed";
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetForRetryAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var delivery = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId
                 && d.ConsumerId == consumerId
                 && (d.State == DeliveryState.Failed || d.State == DeliveryState.DeadLetter),
            cancellationToken);
        if (delivery is null)
        {
            return;
        }

        delivery.State = DeliveryState.Pending;
        delivery.NextAttemptAt = DateTimeOffset.UtcNow;
        delivery.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AcknowledgeAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var delivery = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId && d.ConsumerId == consumerId,
            cancellationToken);
        if (delivery is null)
        {
            return;
        }

        delivery.State = DeliveryState.Cancelled;
        delivery.LastError = "Acknowledged";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryRecord>> ListFailedAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Deliveries
            .Where(d => d.State == DeliveryState.Failed || d.State == DeliveryState.DeadLetter)
            .OrderByDescending(d => d.LastAttemptAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<DeliveryRecord?> GetAsync(Guid eventId, string consumerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Deliveries.FirstOrDefaultAsync(
            d => d.EventId == eventId && d.ConsumerId == consumerId,
            cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    private static DeliveryRecord ToRecord(DeliveryEntity e) =>
        new(
            e.EventId,
            e.ConsumerId,
            e.EventType,
            e.Version,
            e.State,
            e.Attempts,
            e.LastAttemptAt,
            e.NextAttemptAt,
            e.LastError,
            e.CorrelationId,
            e.CreatedAt);

    private static string Truncate(string error) =>
        error.Length <= 2000 ? error : error[..2000];
}
