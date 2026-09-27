using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Review.Application;

/// <summary>
/// Projects provider-neutral check updates onto the matching Change revision.
/// Stale SHA updates do not overwrite the current revision's check status.
/// </summary>
public sealed class CheckProjectionHandler(IChangeRepository repository) : IEventHandler<CheckUpdatedEvent>
{
    public string ConsumerId => "forgedeck.review.check-projection";

    public Task HandleAsync(EventEnvelope<CheckUpdatedEvent> envelope, CancellationToken cancellationToken = default)
    {
        var e = envelope.Data;
        if (e.ChangeId is not Guid changeId)
        {
            return Task.CompletedTask;
        }

        var change = repository.Find(changeId);
        if (change is null)
        {
            return Task.CompletedTask;
        }

        // Associate with the revision SHA carried on the event; never claim current head for a stale commit.
        if (!string.Equals(change.HeadCommit, e.CommitSha, StringComparison.OrdinalIgnoreCase))
        {
            change.RecordActivity(
                "CheckUpdatedStale",
                e.Provider,
                $"{e.Name}={e.Status} for {Short(e.CommitSha)} (current {Short(change.HeadCommit)}).");
            repository.Update(change);
            return Task.CompletedTask;
        }

        change.RecordActivity("CheckUpdated", e.Provider, $"{e.Name}={e.Status} for {Short(e.CommitSha)}.");
        repository.Update(change);
        return Task.CompletedTask;
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}
