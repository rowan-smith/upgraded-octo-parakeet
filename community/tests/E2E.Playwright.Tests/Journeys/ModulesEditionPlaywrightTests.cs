using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

[Collection("playwright")]
public sealed class ModulesEditionPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;
    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task Modules_page_shows_licence_driven_copy()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Edition is licence-driven");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("One card per capability");
    }

    [Theory]
    [InlineData("forgedeck.review")]
    [InlineData("forgedeck.build")]
    [InlineData("forgedeck.deploy")]
    [InlineData("forgedeck.code")]
    public async Task Installed_or_available_base_modules_render_single_card(string extensionId)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        await Assertions.Expect(page.Locator($"[data-extension-id='{extensionId}']")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData("forgedeck.review.team")]
    [InlineData("forgedeck.review.enterprise")]
    [InlineData("forgedeck.review.commercial")]
    [InlineData("forgedeck.build.team")]
    [InlineData("forgedeck.build.enterprise")]
    [InlineData("forgedeck.deploy.team")]
    [InlineData("forgedeck.deploy.enterprise")]
    public async Task Tier_clone_rows_are_not_listed(string extensionId)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        await Assertions.Expect(page.Locator($"[data-extension-id='{extensionId}']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Review_card_shows_edition_pill()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        var card = page.Locator("[data-extension-id='forgedeck.review']");
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator(".module-edition")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Modules_list_has_no_duplicate_review_names()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        var reviewCards = page.Locator(".module-card").Filter(new() { HasText = "Review" });
        // Base Review card only — not "Review Team" / "Review Enterprise" catalogue clones.
        await Assertions.Expect(page.Locator("[data-extension-id='forgedeck.review']")).ToHaveCountAsync(1);
        await Assertions.Expect(reviewCards).ToHaveCountAsync(1);
    }
}
