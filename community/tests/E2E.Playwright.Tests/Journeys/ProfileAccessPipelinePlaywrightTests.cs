using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

[Collection("playwright")]
public sealed class ProfileThemeLicencePlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private async Task<PageSession> NewSessionAsync() => await seeded.Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task Profile_page_saves_display_name()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/profile");
        await Assertions.Expect(page.Locator("#profilePage")).ToBeVisibleAsync();
        await page.Locator("#profileDisplayName").FillAsync("Maya Updated");
        await page.Locator("#profileSave").ClickAsync();
        await Assertions.Expect(page.Locator("#userName")).ToContainTextAsync("Maya Updated");
    }

    [Fact]
    public async Task User_menu_has_theme_flyout_without_settings()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#userMenu").ClickAsync();
        await Assertions.Expect(page.Locator("#userMenuDropdown [data-user-action='settings']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#themeSubmenu")).ToBeVisibleAsync();
        await page.Locator("#themeSubmenu").HoverAsync();
        await Assertions.Expect(page.Locator("[data-theme-choice='dark']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Licence_upload_uses_dropzone()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/license");
        await Assertions.Expect(page.Locator("#licenceDropzone")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#licensingBrowse")).ToBeVisibleAsync();
    }
}
