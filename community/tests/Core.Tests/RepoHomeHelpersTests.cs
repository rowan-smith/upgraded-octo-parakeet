using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Search;

namespace Core.Tests;

public sealed class PullRequestFiltersTests
{
    [Theory]
    [InlineData("Open", "Maya Chen", "assigned", true)]
    [InlineData("Open", "Rowan", "assigned", false)]
    [InlineData("Draft", "Maya Chen", "draft", true)]
    [InlineData("Open", "Maya Chen", "draft", false)]
    [InlineData("Open", "Maya Chen", "open", true)]
    [InlineData("Approved", "Maya Chen", "open", true)]
    [InlineData("Changes Requested", "Maya Chen", "open", true)]
    [InlineData("Draft", "Maya Chen", "open", true)]
    [InlineData("Merged", "Maya Chen", "open", false)]
    [InlineData("Closed", "Maya Chen", "open", false)]
    [InlineData("Closed", "Maya Chen", "abandoned", true)]
    [InlineData("Merged", "Maya Chen", "abandoned", false)]
    [InlineData("Open", "Maya Chen", "abandoned", false)]
    [InlineData("Merged", "Maya Chen", "all", true)]
    [InlineData("Closed", "Maya Chen", "all", true)]
    [InlineData("Open", "Maya Chen", "all", true)]
    [InlineData("Open", "Maya Chen", "", true)]
    [InlineData("Merged", "Maya Chen", "", false)]
    [InlineData("Open", "maya chen", "assigned", true)]
    [InlineData("Draft", "Other", "draft", true)]
    public void Matches_filter_matrix(string status, string actor, string filter, bool expected)
    {
        var reviewers = new[] { "Maya Chen", "Alex" };
        Assert.Equal(expected, PullRequestFilters.Matches(filter, status, reviewers, actor));
    }

    [Fact]
    public void Apply_filters_collection()
    {
        var items = new[]
        {
            new Sample("a", "Open", ["Maya"]),
            new Sample("b", "Draft", ["Rowan"]),
            new Sample("c", "Closed", ["Maya"]),
            new Sample("d", "Merged", ["Maya"])
        };

        var open = PullRequestFilters.Apply(items, "open", "Maya", x => x.Status, x => x.Reviewers).Select(x => x.Id).ToArray();
        Assert.Equal(["a", "b"], open);

        var assigned = PullRequestFilters.Apply(items, "assigned", "Maya", x => x.Status, x => x.Reviewers).Select(x => x.Id).ToArray();
        Assert.Equal(["a", "c", "d"], assigned);

        var abandoned = PullRequestFilters.Apply(items, "abandoned", "Maya", x => x.Status, x => x.Reviewers).Select(x => x.Id).ToArray();
        Assert.Equal(["c"], abandoned);
    }

    [Theory]
    [InlineData("Open", true)]
    [InlineData("Draft", true)]
    [InlineData("Approved", true)]
    [InlineData("Changes Requested", true)]
    [InlineData("Merged", false)]
    [InlineData("Closed", false)]
    public void IsOpen_matrix(string status, bool expected) =>
        Assert.Equal(expected, PullRequestFilters.IsOpen(status));

    [Theory]
    [InlineData("Closed", true)]
    [InlineData("Merged", false)]
    [InlineData("Open", false)]
    public void IsAbandoned_matrix(string status, bool expected) =>
        Assert.Equal(expected, PullRequestFilters.IsAbandoned(status));

    private sealed record Sample(string Id, string Status, string[] Reviewers);
}

public sealed class ShellChromeRulesTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShowProjectSwitcher_hidden_on_org_routes(bool isOrgRoute, bool expected) =>
        Assert.Equal(expected, ShellChromeRules.ShowProjectSwitcher(isOrgRoute));

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void ShowSwitcherChevron_requires_project_shell(bool isOrgRoute, bool enabled, bool expected) =>
        Assert.Equal(expected, ShellChromeRules.ShowSwitcherChevron(isOrgRoute, enabled));
}

public sealed class ModuleCatalogueFilterTests
{
    [Theory]
    [InlineData("forgedeck.review.team", true)]
    [InlineData("forgedeck.review.enterprise", true)]
    [InlineData("forgedeck.review.commercial", true)]
    [InlineData("forgedeck.build.team", true)]
    [InlineData("forgedeck.build.enterprise", true)]
    [InlineData("forgedeck.deploy.team", true)]
    [InlineData("forgedeck.deploy.enterprise", true)]
    [InlineData("forgedeck.review", false)]
    [InlineData("forgedeck.build", false)]
    [InlineData("forgedeck.code", false)]
    [InlineData("forgedeck.git", false)]
    public void Tier_clone_ids_are_unlisted_when_module_type(string extensionId, bool expected)
    {
        var entry = BuiltinExtensionCatalogue.Find(extensionId);
        Assert.NotNull(entry);
        Assert.Equal(expected, ExtensionLifecycleService.IsUnlistedTierClone(entry));
    }

    [Fact]
    public void Connectors_are_never_treated_as_tier_clones()
    {
        var github = BuiltinExtensionCatalogue.Find("forgedeck.github");
        Assert.NotNull(github);
        Assert.Equal(ExtensionType.Connector, github.Type);
        Assert.False(ExtensionLifecycleService.IsUnlistedTierClone(github));
    }

    [Fact]
    public void Catalogue_still_contains_tier_package_ids_for_install_apis()
    {
        Assert.Contains(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.review.team");
        Assert.Contains(BuiltinExtensionCatalogue.All, e => e.ExtensionId == "forgedeck.review.enterprise");
    }
}
