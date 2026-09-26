namespace ForgeDeck.Core.Search;

/// <summary>Pure helpers for overview PR panel filters (Assigned / Draft / Open / Abandoned / All).</summary>
public static class PullRequestFilters
{
    public const string AssignedToMe = "assigned";
    public const string Draft = "draft";
    public const string Open = "open";
    public const string Abandoned = "abandoned";
    public const string All = "all";

    public static bool Matches(
        string filter,
        string status,
        IEnumerable<string> reviewerNames,
        string actor)
    {
        var normalized = string.IsNullOrWhiteSpace(filter) ? Open : filter.Trim().ToLowerInvariant();
        var reviewers = reviewerNames as IList<string> ?? reviewerNames.ToArray();

        return normalized switch
        {
            AssignedToMe => reviewers.Any(name =>
                name.Equals(actor, StringComparison.OrdinalIgnoreCase)),
            Draft => status.Equals("Draft", StringComparison.OrdinalIgnoreCase),
            Open => IsOpen(status),
            Abandoned => IsAbandoned(status),
            All => true,
            _ => IsOpen(status)
        };
    }

    public static IEnumerable<T> Apply<T>(
        IEnumerable<T> changes,
        string filter,
        string actor,
        Func<T, string> status,
        Func<T, IEnumerable<string>> reviewers) =>
        changes.Where(change => Matches(filter, status(change), reviewers(change), actor));

    public static bool IsOpen(string status) =>
        !status.Equals("Merged", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("Closed", StringComparison.OrdinalIgnoreCase);

    public static bool IsAbandoned(string status) =>
        status.Equals("Closed", StringComparison.OrdinalIgnoreCase);
}
