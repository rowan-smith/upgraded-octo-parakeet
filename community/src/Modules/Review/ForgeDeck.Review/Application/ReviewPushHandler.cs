using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Review.Contracts.Events;

namespace ForgeDeck.Review.Application;

/// <summary>
/// Updates open changes when a git push affects their source branch tip.
/// </summary>
public sealed class ReviewPushHandler(IChangeRepository repository, IEventPublisher events) : IEventHandler<GitRepositoryPushEvent>
{
    public string ConsumerId => "forgedeck.review.git-push";

    public async Task HandleAsync(EventEnvelope<GitRepositoryPushEvent> envelope, CancellationToken cancellationToken = default)
    {
        var push = envelope.Data;
        var matches = repository.List()
            .Where(c => !c.Status.Equals("Merged", StringComparison.OrdinalIgnoreCase)
                        && !c.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(c.SourceBranch, push.Branch, StringComparison.OrdinalIgnoreCase)
                        && ($"{c.Repository.Owner}/{c.Repository.Name}".Equals(push.Repository, StringComparison.OrdinalIgnoreCase)
                            || c.Repository.Name.Equals(push.Repository, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Id)
            .ToArray();

        foreach (var changeId in matches)
        {
            string? previous = null;
            var updated = repository.Mutate(changeId, change =>
            {
                previous = change.HeadCommit;
                if (string.Equals(previous, push.CommitSha, StringComparison.OrdinalIgnoreCase))
                {
                    previous = null;
                    return;
                }

                change.RecordActivity("RevisionUpdated", "Git", $"Head moved {Short(previous)} → {Short(push.CommitSha)}.");
                change.HeadCommit = push.CommitSha;
            });

            if (updated is null || previous is null)
            {
                continue;
            }

            await events.PublishAsync(
                new ReviewRevisionUpdatedEvent(
                    changeId,
                    push.ProjectKey,
                    push.Repository,
                    updated.SourceBranch,
                    updated.TargetBranch,
                    push.CommitSha,
                    previous),
                new PublishOptions
                {
                    Actor = new EventActor(ActorType.Extension, "forgedeck.review", "Review"),
                    Publisher = "forgedeck.review"
                },
                cancellationToken);
        }
    }

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];
}
