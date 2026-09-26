using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

[Collection("playwright")]
public sealed class ContextSwitcherPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;
    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    [Theory]
    [InlineData("/home")]
    [InlineData("/projects")]
    [InlineData("/people")]
    [InlineData("/organisation/settings")]
    [InlineData("/organisation/settings/modules")]
    [InlineData("/organisation/settings/license")]
    [InlineData("/organisation/settings/build")]
    [InlineData("/runners")]
    public async Task Context_switcher_hidden_on_org_routes(string route)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator("#contextButton")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("/overview")]
    [InlineData("/changes")]
    [InlineData("/pipelines")]
    [InlineData("/files")]
    [InlineData("/settings")]
    [InlineData("/settings/general")]
    [InlineData("/runs")]
    [InlineData("/queue")]
    public async Task Context_switcher_visible_on_project_routes(string route)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator("#contextButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#contextButton .chevron")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#projectLabel")).Not.ToBeEmptyAsync();
    }

    [Fact]
    public async Task Project_switcher_modal_lists_projects_and_org_home()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#contextButton").ClickAsync();
        await Assertions.Expect(page.Locator("#modal h2")).ToContainTextAsync("Switch project");
        await Assertions.Expect(page.Locator("#modal button[value='home']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#modal button[value='new']")).ToBeVisibleAsync();
    }
}
