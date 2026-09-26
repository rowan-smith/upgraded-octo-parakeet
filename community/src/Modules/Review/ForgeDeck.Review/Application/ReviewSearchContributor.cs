using ForgeDeck.Contracts.Search;

namespace ForgeDeck.Review.Application;

public sealed class ReviewSearchContributor(ReviewService review) : ISearchContributor
{
    public string Source => "review";

    public IReadOnlyList<SearchHit> Search(string query, int limit)
    {
        var open = review.List()
            .Where(change => !string.Equals(change.Status, "Merged", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(change.Status, "Closed", StringComparison.OrdinalIgnoreCase))
            .Where(change =>
                change.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || change.ExternalNumber.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                || change.ExternalId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || change.Author.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(change => new SearchHit(
                "pr",
                change.Id.ToString("D"),
                $"#{change.ExternalNumber} {change.Title}",
                $"/changes/{change.Id:D}",
                $"{change.Status} · {change.Author}"))
            .ToArray();
        return open;
    }
}
