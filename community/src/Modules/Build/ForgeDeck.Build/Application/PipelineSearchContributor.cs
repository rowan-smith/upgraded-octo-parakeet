using ForgeDeck.Contracts.Search;

namespace ForgeDeck.Build.Application;

public sealed class PipelineSearchContributor(PipelineService pipelines) : ISearchContributor
{
    public string Source => "pipelines";

    public IReadOnlyList<SearchHit> Search(string query, int limit)
    {
        return pipelines.ListDefinitions()
            .Where(definition => definition.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(definition => new SearchHit(
                "pipeline",
                definition.Id.ToString("D"),
                definition.Name,
                $"/pipelines/{definition.Id:D}",
                definition.Enabled ? "Enabled" : "Disabled"))
            .ToArray();
    }
}
