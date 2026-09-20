namespace Modules.Review.Domain;

public sealed class SingleApprovalPolicy
{
    public ApprovalPolicyResult Evaluate(Change change)
    {
        var latest = change.Reviews.GroupBy(review => review.Reviewer, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(review => review.CreatedAt).First()).ToArray();
        return new(latest.Any(review => review.State == "Approved"),
            latest.Any(review => review.State == "ChangesRequested"), change.ProviderMergeable);
    }
}
