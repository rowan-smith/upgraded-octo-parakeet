using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

[Collection("playwright")]
public sealed class SearchPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;
    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task Global_search_is_editable()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#globalSearch")).ToBeEditableAsync();
    }

    [Theory]
    [InlineData("Control+k")]
    [InlineData("Meta+k")]
    public async Task Shortcut_focuses_global_search(string shortcut)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Keyboard.PressAsync(shortcut);
        await Assertions.Expect(page.Locator("#globalSearch")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Empty_search_shows_shortcut_hits()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#globalSearch").ClickAsync();
        await Assertions.Expect(page.Locator("#searchPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#searchPanel .search-hit").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#searchPanel")).ToContainTextAsync("Pull requests");
    }

    [Theory]
    [InlineData("ForgeDeck")]
    [InlineData("forgedeck")]
    [InlineData("forge")]
    public async Task Search_project_hit_navigates_to_overview(string query)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/changes");
        await page.Locator("#globalSearch").FillAsync(query);
        await page.WaitForSelectorAsync("#searchPanel .search-hit");
        await page.Locator("#searchPanel .search-hit").Filter(new() { HasText = "ForgeDeck" }).First.ClickAsync();
        await Assertions.Expect(page.Locator("#repoHome")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    [Fact]
    public async Task Search_enter_activates_first_hit()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#globalSearch").FillAsync("");
        await page.Locator("#globalSearch").FocusAsync();
        await page.WaitForSelectorAsync("#searchPanel .search-hit");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => !document.getElementById('searchPanel') || document.getElementById('searchPanel').hidden");
    }

    [Theory]
    [InlineData("Pull requests")]
    [InlineData("Pipelines")]
    [InlineData("Projects")]
    [InlineData("Project settings")]
    public async Task Shortcut_hit_types_are_present_for_empty_query(string needle)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await page.Locator("#globalSearch").ClickAsync();
        await Assertions.Expect(page.Locator("#searchPanel")).ToContainTextAsync(needle);
    }
}
