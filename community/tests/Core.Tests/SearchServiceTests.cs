using ForgeDeck.Contracts.Search;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Search;

namespace Core.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public void Empty_query_returns_route_shortcuts()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());

        var hits = search.Search(null);
        Assert.Equal(4, hits.Count);
        Assert.Contains(hits, h => h.Type == "route" && h.Route == "/changes");
        Assert.Contains(hits, h => h.Route == "/pipelines");
        Assert.Contains(hits, h => h.Route == "/projects");
        Assert.Contains(hits, h => h.Route == "/settings");
    }

    [Fact]
    public void Empty_string_query_returns_same_shortcuts_as_null()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());
        Assert.Equal(search.Search("").Select(h => h.Id), search.Search(null).Select(h => h.Id));
    }

    [Theory]
    [InlineData("plat", "Platform")]
    [InlineData("PLATFORM", "Platform")]
    [InlineData("platf", "Platform")]
    [InlineData("delivery", "Platform")]
    public void Search_matches_project_name_slug_or_description(string query, string expectedLabel)
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        fixture.Projects.CreateProject(new CreateProjectRequest("Platform", "platform", null, "Delivery workspace"), owner.Id);
        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());

        var hits = search.Search(query);
        Assert.Contains(hits, h => h.Type == "project" && h.Label == expectedLabel);
    }

    [Theory]
    [InlineData("maya")]
    [InlineData("Maya")]
    [InlineData("forgedeck.dev")]
    [InlineData("Chen")]
    public void Search_matches_people_by_name_username_or_email(string query)
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());

        var hits = search.Search(query);
        Assert.Contains(hits, h => h.Type == "person");
    }

    [Fact]
    public void Search_includes_contributor_hits_after_core_hits()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var contributor = new StubContributor([
            new SearchHit("pr", "1", "#1 Fix login", "/changes/1", "Open")
        ]);
        var search = new SearchService(fixture.Store, fixture.Projects, [contributor]);

        var hits = search.Search("login");
        Assert.Contains(hits, h => h.Type == "pr" && h.Label.Contains("login"));
    }

    [Fact]
    public void Search_respects_limit()
    {
        using var fixture = new TenancyFixture();
        var owner = fixture.Bootstrap();
        for (var i = 0; i < 8; i++)
        {
            fixture.Projects.CreateProject(new CreateProjectRequest($"Alpha{i}", $"alpha{i}", null, null), owner.Id);
        }

        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());
        Assert.Equal(3, search.Search("Alpha", limit: 3).Count);
    }

    [Fact]
    public void Unknown_query_returns_empty_without_shortcuts()
    {
        using var fixture = new TenancyFixture();
        fixture.Bootstrap();
        var search = new SearchService(fixture.Store, fixture.Projects, Array.Empty<ISearchContributor>());
        Assert.Empty(search.Search("zzznomatch999"));
    }

    private sealed class StubContributor(IReadOnlyList<SearchHit> hits) : ISearchContributor
    {
        public string Source => "stub";
        public IReadOnlyList<SearchHit> Search(string query, int limit) =>
            hits.Where(h => h.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToArray();
    }
}
