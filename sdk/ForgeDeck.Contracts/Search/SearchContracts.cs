namespace ForgeDeck.Contracts.Search;

public sealed record SearchHit(
    string Type,
    string Id,
    string Label,
    string Route,
    string? Subtitle = null);

public interface ISearchContributor
{
    string Source { get; }
    IReadOnlyList<SearchHit> Search(string query, int limit);
}

public interface ISearchService
{
    IReadOnlyList<SearchHit> Search(string? query, int limit = 20);
}
