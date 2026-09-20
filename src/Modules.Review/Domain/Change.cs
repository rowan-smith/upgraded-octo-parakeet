using System.Text.Json.Serialization;
using Platform.Contracts.SourceControl;

namespace Modules.Review.Domain;

public sealed class Change
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public required string ExternalId { get; init; }
    public int ExternalNumber { get; set; }
    public required string ExternalUrl { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Author { get; set; }
    public required string SourceBranch { get; set; }
    public required string TargetBranch { get; set; }
    public required string HeadCommit { get; set; }
    public required string BaseCommit { get; set; }
    public required SourceRepository Repository { get; init; }
    [JsonInclude] public string Status { get; private set; } = "Open";
    public bool ProviderMergeable { get; set; } = true;
    public List<SourceDiffFile> Files { get; init; } = [];
    public List<ChangeComment> Comments { get; init; } = [];
    public List<Reviewer> Reviewers { get; init; } = [];
    public List<SubmittedReview> Reviews { get; init; } = [];
    public List<ChangeActivity> Activity { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonInclude] public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? MergedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset LastSynchronizedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? MergeCommitSha { get; set; }
    public string CommitSha => HeadCommit;
    public IReadOnlyList<Approval> Approvals => Reviews.Where(review => review.State == "Approved").Select(review => new Approval(review.Id, review.Reviewer, review.CreatedAt)).ToArray();
    public ApprovalPolicyResult MergePolicy => new SingleApprovalPolicy().Evaluate(this);
    public bool CanMerge => Status is not ("Merged" or "Closed") && MergePolicy.Satisfied;

    public static Change FromExternal(Guid projectId, SourceRepository repository, ExternalChange external)
    {
        var change = new Change
        {
            Id = Guid.NewGuid(), ProjectId = projectId, ExternalId = external.ExternalId, ExternalNumber = external.ExternalNumber,
            ExternalUrl = external.ExternalUrl, Title = external.Title, Description = external.Description, Author = external.Author,
            SourceBranch = external.SourceBranch, TargetBranch = external.TargetBranch, HeadCommit = external.HeadCommit, BaseCommit = external.BaseCommit,
            Repository = repository, Status = external.Status, ProviderMergeable = external.IsMergeable, CreatedAt = external.CreatedAt,
            UpdatedAt = external.UpdatedAt, MergedAt = external.MergedAt, ClosedAt = external.ClosedAt,
            LastSynchronizedAt = DateTimeOffset.UtcNow, Files = [.. external.Diff?.Files ?? []]
        };
        change.Record("ChangeCreated", external.Author, "Change imported from source provider.");
        return change;
    }

    public ChangeComment AddComment(string author, string body, string? file = null, string? side = null, int? line = null, string? commitSha = null, Guid? parentId = null)
    {
        var parent = parentId is null ? null : Comments.FirstOrDefault(comment => comment.Id == parentId) ?? throw new InvalidOperationException("Parent comment was not found.");
        var comment = new ChangeComment(Guid.NewGuid(), parent?.DiscussionId ?? Guid.NewGuid(), parentId, author, body, DateTimeOffset.UtcNow, file, side, line, commitSha ?? HeadCommit);
        Apply(() => Comments.Add(comment)); Record("CommentAdded", author, file is null ? "General comment added." : $"Inline comment added on {file}:{line}."); return comment;
    }

    public void SetDiscussionResolution(Guid discussionId, string actor, bool resolved)
    {
        var matches = Comments.Where(comment => comment.DiscussionId == discussionId).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException("Discussion was not found.");
        var at = resolved ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        Apply(() => { foreach (var comment in matches) Comments[Comments.IndexOf(comment)] = comment with { ResolvedAt = at }; });
        Record(resolved ? "DiscussionResolved" : "DiscussionReopened", actor, resolved ? "Discussion resolved." : "Discussion reopened.");
    }

    public void AddReviewer(string actor, string name)
    {
        if (Reviewers.All(reviewer => !reviewer.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) Apply(() => Reviewers.Add(new(Guid.NewGuid(), name, "Requested")));
        Record("ReviewerRequested", actor, $"{name} was requested as reviewer.");
    }

    public void RemoveReviewer(string actor, string name)
    {
        Apply(() => Reviewers.RemoveAll(reviewer => reviewer.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        Record("ReviewerRemoved", actor, $"{name} was removed as reviewer.");
    }

    public void SubmitReview(string reviewer, string state, string? body)
    {
        if (state is not ("Comment" or "Approved" or "ChangesRequested")) throw new ArgumentException("Review state must be Comment, Approved, or ChangesRequested.");
        Apply(() =>
        {
            Reviews.Add(new(Guid.NewGuid(), reviewer, state, body, DateTimeOffset.UtcNow));
            var index = Reviewers.FindIndex(value => value.Name.Equals(reviewer, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) Reviewers[index] = Reviewers[index] with { Status = state };
            if (!string.IsNullOrWhiteSpace(body)) Comments.Add(new(Guid.NewGuid(), Guid.NewGuid(), null, reviewer, body, DateTimeOffset.UtcNow, null, null, null, HeadCommit));
            var policy = new SingleApprovalPolicy().Evaluate(this);
            Status = state == "ChangesRequested" ? "Changes Requested" : policy.HasApproval && !policy.HasBlockingReview ? "Approved" : Status;
        });
        Record(state, reviewer, state == "Approved" ? "Review approved." : state == "ChangesRequested" ? "Changes requested." : "Review submitted.");
    }

    public void Approve(string reviewer) => SubmitReview(reviewer, "Approved", null);
    public void RequestChanges(string reviewer, string body) => SubmitReview(reviewer, "ChangesRequested", body);

    public void Synchronize(ExternalChange external)
    {
        var newCommits = HeadCommit != external.HeadCommit;
        Apply(() =>
        {
            Title = external.Title; Description = external.Description; SourceBranch = external.SourceBranch; TargetBranch = external.TargetBranch;
            HeadCommit = external.HeadCommit; BaseCommit = external.BaseCommit; ProviderMergeable = external.IsMergeable; LastSynchronizedAt = DateTimeOffset.UtcNow;
            MergedAt = external.MergedAt; ClosedAt = external.ClosedAt; Status = external.Status;
            if (external.Diff is not null) { Files.Clear(); Files.AddRange(external.Diff.Files); }
        });
        if (newCommits) Record("CommitsUpdated", external.Author, "New commits detected from source provider.");
    }

    public void MarkMerged(string actor, string? commitSha)
    {
        if (!CanMerge) throw new InvalidOperationException("One approval, no active changes-requested review, and a mergeable source branch are required.");
        Apply(() => { Status = "Merged"; MergedAt = DateTimeOffset.UtcNow; MergeCommitSha = commitSha; }); Record("ChangeMerged", actor, $"Merged into {TargetBranch}.");
    }

    public void MarkClosed(string actor) { Apply(() => { Status = "Closed"; ClosedAt = DateTimeOffset.UtcNow; }); Record("ChangeClosed", actor, "Change closed without merge."); }

    private void Record(string type, string actor, string detail) => Activity.Add(new(Guid.NewGuid(), type, actor, detail, DateTimeOffset.UtcNow));
    private void Apply(Action mutation) { mutation(); UpdatedAt = DateTimeOffset.UtcNow; }
}
