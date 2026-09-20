using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class SetupOnboardingPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Welcome_screen_shown_on_fresh_install()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.ExpectVisible(page, "h1", "Welcome");
        await Assertions.Expect(page.Locator("#setupStart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#appSidebar")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Full_setup_creates_organisation_owner_and_first_project()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Northstar Engineering", email: "setup-owner@example.com", username: "setupowner");

        await Assertions.Expect(page.Locator("#content h1").First).ToContainTextAsync("Platform");
        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Northstar Engineering");
        await Assertions.Expect(page.Locator("#userName")).ToHaveTextAsync("Rowan Smith");
    }

    [Fact]
    public async Task Setup_back_returns_to_welcome()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#setupStart").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create your organisation");
        await page.Locator("#setupBack").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Welcome");
    }

    [Fact]
    public async Task Password_mismatch_shows_toast_and_stays_on_setup()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#setupStart").ClickAsync();
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("different");
        await page.Locator("#setupContinue").ClickAsync();
        await Ui.ExpectToastAsync(page, "Passwords do not match");
        await Ui.ExpectVisible(page, "h1", "Create your organisation");
    }

    [Fact]
    public async Task Skip_project_still_enters_workspace()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#setupStart").ClickAsync();
        await page.Locator("#setupOrgName").FillAsync("Skip Org");
        await page.Locator("#setupDisplayName").FillAsync("Skip Owner");
        await page.Locator("#setupUsername").FillAsync("skipowner");
        await page.Locator("#setupEmail").FillAsync("skip@example.com");
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("password123");
        await page.Locator("#setupContinue").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create your first project");
        await page.Locator("#setupSkipProject").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Skip Org");
    }
}
