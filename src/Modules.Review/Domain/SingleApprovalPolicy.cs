namespace Modules.Review.Domain;

public sealed class SingleApprovalPolicy : IApprovalPolicy
{
    public ApprovalPolicyResult Evaluate(Change change)
    {
        var latest = ReviewPolicySupport.LatestReviews(change);
        return new(latest.Any(review => review.State == "Approved"),
            latest.Any(review => review.State == "ChangesRequested"), change.ProviderMergeable, RequiredChecks: null);
    }
}
