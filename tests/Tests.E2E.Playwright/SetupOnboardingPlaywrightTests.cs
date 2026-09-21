using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class SetupOnboardingPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Bootstrap_login_shown_on_fresh_install()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.ExpectVisible(page, "h1", "Initial Setup");
        await Assertions.Expect(page.Locator("#bootstrapSignIn")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#bootstrapWarning")).ToBeVisibleAsync();
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
    public async Task Licence_step_follows_organisation()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Set up your organisation");
        await page.Locator("#setupOrgName").FillAsync("Licence Org");
        await page.Locator("#setupOrgContinue").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Choose your licence");
        await Assertions.Expect(page.Locator("#setupUseCommunity")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".setup-org-brand")).ToContainTextAsync("Licence Org");
    }

    [Fact]
    public async Task Password_mismatch_shows_toast_and_stays_on_owner()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await page.Locator("#setupOrgContinue").ClickAsync();
        await page.Locator("#setupUseCommunity").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create Owner Account");
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("different");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectToastAsync(page, "Passwords do not match");
        await Ui.ExpectVisible(page, "h1", "Create Owner Account");
    }

    [Fact]
    public async Task Skip_project_still_enters_workspace()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await page.Locator("#setupOrgName").FillAsync("Skip Org");
        await page.Locator("#setupOrgContinue").ClickAsync();
        await page.Locator("#setupUseCommunity").ClickAsync();
        await page.Locator("#setupDisplayName").FillAsync("Skip Owner");
        await page.Locator("#setupUsername").FillAsync("skipowner");
        await page.Locator("#setupEmail").FillAsync("skip@example.com");
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("password123");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create your first project");
        await page.Locator("#setupSkipProject").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Skip Org");
    }

    [Fact]
    public async Task Bootstrap_credentials_rejected_after_owner_created()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await page.Locator("#setupOrgContinue").ClickAsync();
        await page.Locator("#setupUseCommunity").ClickAsync();
        await page.Locator("#setupUsername").FillAsync("ownerone");
        await page.Locator("#setupEmail").FillAsync("ownerone@example.com");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create your first project");

        var response = await page.APIRequest.PostAsync($"{host.BaseAddress}api/setup/bootstrap-login", new()
        {
            DataObject = new { username = "admin", password = "admin" }
        });
        Assert.Equal(409, response.Status);
    }
}
