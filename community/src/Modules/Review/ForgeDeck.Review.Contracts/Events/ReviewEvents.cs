using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Review.Contracts.Events;

public sealed record ReviewRequestedEvent(
    Guid ChangeId,
    string ProjectKey,
    string Repository,
    string SourceBranch,
    string TargetBranch,
    string CommitSha,
    string? RepositoryUrl = null);

public sealed record ReviewApprovedEvent(
    Guid ChangeId,
    string ProjectKey,
    string ApproverId,
    string ApproverDisplayName);

public sealed record ReviewChangesRequestedEvent(
    Guid ChangeId,
    string ProjectKey,
    string ReviewerId,
    string? Comment = null);

public sealed record ReviewCommentAddedEvent(
    Guid ChangeId,
    string ProjectKey,
    string AuthorId,
    string Body);

public sealed record ReviewMergedEvent(
    Guid ChangeId,
    string ProjectKey,
    string CommitSha);

public sealed record ReviewRevisionUpdatedEvent(
    Guid ChangeId,
    string ProjectKey,
    string Repository,
    string SourceBranch,
    string TargetBranch,
    string CommitSha,
    string PreviousCommitSha,
    string? RepositoryUrl = null);

public static class ReviewEventContracts
{
    public static readonly EventContract<ReviewRequestedEvent> Requested =
        new("forgedeck.review.requested", 1);
    public static readonly EventContract<ReviewApprovedEvent> Approved =
        new("forgedeck.review.approved", 1);
    public static readonly EventContract<ReviewChangesRequestedEvent> ChangesRequested =
        new("forgedeck.review.changes-requested", 1);
    public static readonly EventContract<ReviewCommentAddedEvent> CommentAdded =
        new("forgedeck.review.comment-added", 1);
    public static readonly EventContract<ReviewMergedEvent> Merged =
        new("forgedeck.review.merged", 1);
    public static readonly EventContract<ReviewRevisionUpdatedEvent> RevisionUpdated =
        new("forgedeck.review.revision-updated", 1);

    public static IEnumerable<IEventContract> All()
    {
        yield return Requested;
        yield return Approved;
        yield return ChangesRequested;
        yield return CommentAdded;
        yield return Merged;
        yield return RevisionUpdated;
    }
}
