namespace Modules.Review.Domain;

public sealed record ChangeComment(
    Guid Id, Guid DiscussionId, Guid? ParentId, string Author, string Body, DateTimeOffset CreatedAt,
    string? File, string? Side, int? Line, string? CommitSha, DateTimeOffset? ResolvedAt = null,
    DateTimeOffset? EditedAt = null, bool Outdated = false)
{
    public bool Resolved => ResolvedAt is not null;
}

public sealed record Reviewer(Guid Id, string Name, string Status);
public sealed record SubmittedReview(Guid Id, string Reviewer, string State, string? Body, DateTimeOffset CreatedAt);
public sealed record Approval(Guid Id, string Reviewer, DateTimeOffset CreatedAt);
public sealed record ChangeActivity(Guid Id, string Type, string Actor, string Detail, DateTimeOffset CreatedAt);

public sealed record ApprovalPolicyResult(
    bool HasApproval,
    bool HasBlockingReview,
    bool ProviderMergeable,
    IReadOnlyList<CheckRequirementStatus>? RequiredChecks = null)
{
    public bool ChecksSatisfied => RequiredChecks is null || RequiredChecks.Count == 0 || RequiredChecks.All(c => c.Satisfied);
    public bool Satisfied => HasApproval && !HasBlockingReview && ProviderMergeable && ChecksSatisfied;
}

public sealed record CheckRequirementStatus(string Name, bool Satisfied, string Status);

public sealed class ReviewOptions
{
    public const string SectionName = "Review";
    public bool AllowSelfReview { get; set; } = true;
    public int MinimumApprovals { get; set; } = 1;
    public List<string> RequiredChecks { get; set; } = [];
}
