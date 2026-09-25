using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

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
    public async Task Full_setup_reaches_home_and_can_create_project()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Northstar Engineering", email: "setup-owner@example.com", username: "setupowner");

        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Northstar Engineering");
        await Assertions.Expect(page.Locator("#userName")).ToHaveTextAsync("Rowan Smith");
        await Assertions.Expect(page.Locator("#projectLabel")).ToHaveTextAsync("Platform");
    }

    [Fact]
    public async Task Fresh_setup_is_core_only_until_modules_are_installed()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Core Only Org", email: "core@example.com", username: "coreowner", installDefaultModules: false);

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/settings']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToHaveCountAsync(0);

        await Ui.EnsureOrgNavAsync(page);
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Extend ForgeDeck");

        await Ui.OpenModulesAsync(page);
        await Ui.ClickAsync(page.Locator("[data-ext-install='forgedeck.code']"));
        await page.Locator(".modal-content button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-extension-id='forgedeck.code']")).ToContainTextAsync("Enabled");

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToHaveCountAsync(0);
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
    public async Task Modules_step_follows_licence_and_can_be_skipped()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupThroughLicenceAsync(page, "Modules Org");
        await page.Locator("#setupUseCommunity").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Choose capabilities");
        await Assertions.Expect(page.Locator("#setupSkipModules")).ToBeVisibleAsync();
        await page.Locator("#setupSkipModules").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create Owner Account");
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
        await page.Locator("#setupSkipModules").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Create Owner Account");
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("different");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectToastAsync(page, "Passwords do not match");
        await Ui.ExpectVisible(page, "h1", "Create Owner Account");
    }

    [Fact]
    public async Task Lean_setup_without_project_shows_get_started_home()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await page.Locator("#setupOrgName").FillAsync("Skip Org");
        await page.Locator("#setupOrgContinue").ClickAsync();
        await page.Locator("#setupUseCommunity").ClickAsync();
        await page.Locator("#setupSkipModules").ClickAsync();
        await page.Locator("#setupDisplayName").FillAsync("Skip Owner");
        await page.Locator("#setupUsername").FillAsync("skipowner");
        await page.Locator("#setupEmail").FillAsync("skip@example.com");
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("password123");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Skip Org is ready");
        await page.Locator("#setupOpenWorkspace").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await Assertions.Expect(page.Locator("#orgLabel")).ToHaveTextAsync("Skip Org");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Get started");
        await Assertions.Expect(page.Locator("#homeCreateFirstProject")).ToBeVisibleAsync();
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
        await page.Locator("#setupSkipModules").ClickAsync();
        await page.Locator("#setupUsername").FillAsync("ownerone");
        await page.Locator("#setupEmail").FillAsync("ownerone@example.com");
        await page.Locator("#setupCreateOwner").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "is ready");

        var response = await page.APIRequest.PostAsync($"{host.BaseAddress}api/setup/bootstrap-login", new()
        {
            DataObject = new { username = "admin", password = "admin" }
        });
        Assert.Equal(409, response.Status);
    }
}
