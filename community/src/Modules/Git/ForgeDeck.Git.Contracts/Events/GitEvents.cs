using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Git.Contracts.Events;

public sealed record GitRepositoryCreatedEvent(
    Guid RepositoryId,
    string ProjectKey,
    string Repository);

public sealed record GitRepositoryPushEvent(
    Guid RepositoryId,
    string ProjectKey,
    string Repository,
    string Branch,
    string CommitSha,
    string? PreviousCommitSha = null);

public sealed record GitRepositoryRefUpdatedEvent(
    Guid RepositoryId,
    string ProjectKey,
    string Repository,
    string RefName,
    string CommitSha);

public sealed record GitRepositoryDeletedEvent(
    Guid RepositoryId,
    string ProjectKey,
    string Repository);

public static class GitEventContracts
{
    public static readonly EventContract<GitRepositoryCreatedEvent> RepositoryCreated =
        new("forgedeck.git.repository-created", 1);
    public static readonly EventContract<GitRepositoryPushEvent> RepositoryPush =
        new("forgedeck.git.repository-push", 1);
    public static readonly EventContract<GitRepositoryRefUpdatedEvent> RepositoryRefUpdated =
        new("forgedeck.git.repository-ref-updated", 1);
    public static readonly EventContract<GitRepositoryDeletedEvent> RepositoryDeleted =
        new("forgedeck.git.repository-deleted", 1);

    public static IEnumerable<IEventContract> All()
    {
        yield return RepositoryCreated;
        yield return RepositoryPush;
        yield return RepositoryRefUpdated;
        yield return RepositoryDeleted;
    }
}
