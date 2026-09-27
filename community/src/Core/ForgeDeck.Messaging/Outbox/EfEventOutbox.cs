using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Persistence;
using ForgeDeck.Messaging.Serialization;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Messaging.Outbox;

public interface IEventOutbox
{
    Task EnqueueAsync(EventEnvelope envelope, string payloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxRecord>> ClaimPendingAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkDispatchedAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxRecord>> ListRecentAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxRecord>> ListBacklogAsync(CancellationToken cancellationToken = default);
    Task<OutboxRecord?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed class EfEventOutbox(
    IDbContextFactory<MessagingDbContext> factory,
    IEventSerializer serializer) : IEventOutbox
{
    public async Task EnqueueAsync(EventEnvelope envelope, string payloadJson, CancellationToken cancellationToken = default)
    {
        var envelopeJson = serializer.SerializeEnvelope(envelope);
        var entity = new OutboxEntity
        {
            EventId = envelope.Id,
            EventType = envelope.Type,
            Version = envelope.Version,
            EnvelopeJson = envelopeJson,
            PayloadJson = payloadJson,
            CreatedAt = envelope.OccurredAt.UtcDateTime,
            DispatchState = OutboxDispatchState.Pending,
            Attempts = 0,
            NextAttemptAt = envelope.OccurredAt.UtcDateTime
        };

        if (EfUnitOfWork.TryGetAmbient(out var ambient))
        {
            ambient.Outbox.Add(entity);
            await ambient.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Outbox.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxRecord>> ClaimPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var claimed = await db.Outbox
            .Where(o => o.DispatchState == OutboxDispatchState.Pending
                        && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
            .OrderBy(o => o.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var row in claimed)
        {
            row.DispatchState = OutboxDispatchState.Dispatching;
            row.LastAttemptAt = now;
            row.Attempts++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return claimed.Select(ToRecord).ToList();
    }

    public async Task MarkDispatchedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Outbox.FirstOrDefaultAsync(o => o.EventId == eventId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.DispatchState = OutboxDispatchState.Dispatched;
        row.LastError = null;
        row.NextAttemptAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Outbox.FirstOrDefaultAsync(o => o.EventId == eventId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.DispatchState = OutboxDispatchState.Failed;
        row.LastError = Truncate(error);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxRecord>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Outbox
            .OrderByDescending(o => o.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<OutboxRecord>> ListBacklogAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Outbox
            .Where(o => o.DispatchState == OutboxDispatchState.Pending
                        || o.DispatchState == OutboxDispatchState.Dispatching
                        || o.DispatchState == OutboxDispatchState.Failed)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<OutboxRecord?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Outbox.FirstOrDefaultAsync(o => o.EventId == eventId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    private static OutboxRecord ToRecord(OutboxEntity e) =>
        new(
            e.EventId,
            e.EventType,
            e.Version,
            e.EnvelopeJson,
            e.PayloadJson,
            e.CreatedAt,
            e.DispatchState,
            e.Attempts,
            e.LastAttemptAt,
            e.LastError,
            e.NextAttemptAt);

    private static string Truncate(string error) =>
        error.Length <= 2000 ? error : error[..2000];
}
