using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

[Collection("playwright")]
public sealed class OverviewRepoHomePlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;
    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task Overview_renders_repo_home_not_admin_metric_strip()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        await Assertions.Expect(page.Locator("#repoHome")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".repo-pr-filters")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#content > .metric-row")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("assigned", "Assigned to me")]
    [InlineData("draft", "Draft")]
    [InlineData("open", "Open")]
    [InlineData("abandoned", "Abandoned")]
    [InlineData("all", "All")]
    public async Task Overview_pr_filter_buttons_toggle(string filter, string label)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        var button = page.Locator($"[data-pr-filter='{filter}']");
        await Assertions.Expect(button).ToContainTextAsync(label);
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveClassAsync(new Regex("\\bprimary\\b"));
        await Assertions.Expect(page.Locator("#repoHome")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Overview_primary_actions_include_browse_and_prs()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        await Assertions.Expect(page.Locator("#repoHome [data-route='/files']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#repoHome [data-route='/changes']").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Project_settings_overview_shows_delivery_metrics()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/settings/general");
        await Assertions.Expect(page.Locator("#projectDeliveryMetrics")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#projectDeliveryMetrics")).ToContainTextAsync("Delivery metrics");
        await Assertions.Expect(page.Locator("#projectDeliveryMetrics")).ToContainTextAsync("Pipeline success");
    }

    [Fact]
    public async Task Overview_keeps_project_shell()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#contextButton")).ToBeVisibleAsync();
    }
}
