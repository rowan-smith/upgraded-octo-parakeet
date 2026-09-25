namespace ForgeDeck.Review.Domain;

public static class ReviewPolicySupport
{
    public static SubmittedReview[] LatestReviews(Change change) => change.Reviews
        .GroupBy(review => review.Reviewer, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(review => review.CreatedAt).First())
        .ToArray();
}
