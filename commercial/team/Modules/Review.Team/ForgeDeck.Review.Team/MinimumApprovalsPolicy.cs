using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Team;

public sealed class MinimumApprovalsPolicy(int minimumApprovals) : IApprovalPolicy
{
    public int MinimumApprovals { get; } = Math.Max(1, minimumApprovals);

    public ApprovalPolicyResult Evaluate(Change change)
    {
        var latest = ReviewPolicySupport.LatestReviews(change);
        var approvals = latest.Count(review => review.State == "Approved");
        return new(approvals >= MinimumApprovals,
            latest.Any(review => review.State == "ChangesRequested"), change.ProviderMergeable, RequiredChecks: null);
    }
}
