using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class ReviewPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Changes_page_exposes_filters_and_actions()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenChangesAsync(page);
        await Assertions.Expect(page.Locator("#refreshChanges")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#discoverButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#importButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#statusFilter")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#authorFilter")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#reviewerFilter")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Pull Requests");
    }

    [Fact]
    public async Task Changes_filters_update_list_without_errors()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenChangesAsync(page);
        await page.Locator("#statusFilter").SelectOptionAsync(new SelectOptionValue { Label = "Open" });
        await page.Locator("#authorFilter").FillAsync("nobody-matching");
        await Assertions.Expect(page.Locator("#changeList .empty")).ToBeVisibleAsync();

        await page.Locator("#authorFilter").FillAsync("");
        await page.Locator("#statusFilter").SelectOptionAsync(new SelectOptionValue { Label = "All statuses" });
        await Ui.ClickAsync(page.Locator("#refreshChanges"));
        await Ui.ExpectToastAsync(page, "Changes refreshed");
    }

    [Fact]
    public async Task Import_change_modal_opens_with_repository_context()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenChangesAsync(page);
        await Ui.ClickAsync(page.Locator("#importButton"));
        await Ui.ExpectModalOpenAsync(page, "Import GitHub pull request");
        await Assertions.Expect(page.Locator("#modal[open] #externalId")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#modal[open]")).ToContainTextAsync("rowan-smith");
        await page.Locator("#modal[open] button[value='cancel']").ClickAsync();
        await Ui.ExpectModalClosedAsync(page);
    }

    [Fact]
    public async Task Import_invalid_pr_number_shows_error_toast()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenChangesAsync(page);
        await Ui.ClickAsync(page.Locator("#importButton"));
        await page.Locator("#modal[open] #externalId").FillAsync("99999999");
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Assertions.Expect(page.Locator("#toast.error")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 20000 });
    }

    [Fact]
    public async Task Review_queue_renders_three_sections()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenQueueAsync(page);
        await Assertions.Expect(page.Locator(".queue-section")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Waiting for me");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Authored by me");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Recently reviewed");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Review queue");
    }

    [Fact]
    public async Task Branches_page_lists_source_branches()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.NavigateAsync(page, "/source-branches");
        await Ui.ExpectVisible(page, "h1", "Branches");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Branches");
        // Seeded GitHub connection may yield branches or an empty/error state depending on network;
        // the page shell must still render.
        await Assertions.Expect(page.Locator("#content")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Repositories_browser_renders_file_table_and_sidebar()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.NavigateAsync(page, "/files");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Repositories");
        await Assertions.Expect(page.Locator(".file-browser")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 20000 });
        await Assertions.Expect(page.Locator("#refPicker")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".repo-aside")).ToContainTextAsync("About");
        await Assertions.Expect(page.Locator("#cloneButton")).ToBeVisibleAsync();
    }
}

[Collection("playwright")]
public sealed class SettingsPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Settings_shows_local_and_source_sections()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenSettingsAsync(page);
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Local repository");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Source repositories");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("GitHub credential");
        await Assertions.Expect(page.Locator("#connectRepository")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#credentialButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("rowan-smith/upgraded-octo-parakeet");
    }

    [Fact]
    public async Task Connect_repository_modal_opens_with_default_url()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenSettingsAsync(page);
        await Ui.ClickAsync(page.Locator("#connectRepository"));
        await Assertions.Expect(page.Locator(".modal-content h2")).ToHaveTextAsync("Connect repository");
        await Assertions.Expect(page.Locator("#repositoryUrl")).ToHaveValueAsync("https://github.com/rowan-smith/upgraded-octo-parakeet");
        await page.Locator("button[value='cancel']").ClickAsync();
    }

    [Fact]
    public async Task Connect_local_repository_modal_opens()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenSettingsAsync(page);
        var connectLocal = page.Locator("#connectLocal");
        if (await connectLocal.CountAsync() == 0)
        {
            // Already associated in this environment — still assert open-folder/disconnect controls.
            await Assertions.Expect(page.Locator("#settingsOpenFolder").Or(page.Locator("#disconnectLocal")).First).ToBeVisibleAsync();
            return;
        }

        await Ui.ClickAsync(connectLocal);
        await Assertions.Expect(page.Locator(".modal-content h2")).ToHaveTextAsync("Connect local repository");
        await Assertions.Expect(page.Locator("#localPath")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#detectLocal")).ToBeVisibleAsync();
        await page.Locator("button[value='cancel']").ClickAsync();
    }

    [Fact]
    public async Task Credential_modal_opens_from_settings()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenSettingsAsync(page);
        await Ui.ClickAsync(page.Locator("#credentialButton"));
        await Assertions.Expect(page.Locator(".modal-content h2")).ToHaveTextAsync("GitHub personal access token");
        await Assertions.Expect(page.Locator("#githubToken")).ToBeVisibleAsync();
        await page.Locator("button[value='cancel']").ClickAsync();
    }
}

[Collection("playwright")]
public sealed class ShellPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Command_palette_opens_and_jumps_to_changes()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Ui.ClickAsync(page.Locator("#globalSearch"));
        await Assertions.Expect(page.Locator(".modal-content h2")).ToHaveTextAsync("Quick switcher");
        await Ui.ClickAsync(page.Locator("#switcherChanges, button[value='changes']").First);
        await Ui.ExpectVisible(page, "h1", "Pull Requests");
    }

    [Fact]
    public async Task Audit_log_page_renders_heading()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenAuditAsync(page);
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Audit");
        await Assertions.Expect(page.Locator("#content .card")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Modules_page_lists_installed_product_modules()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator(".module-card").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Review");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Build");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Code");
        await Assertions.Expect(page.Locator("#content")).Not.ToContainTextAsync("GitHub");
    }

    [Fact]
    public async Task Connectors_page_lists_github_separately_from_modules()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.GoToHashAsync(page, "/organisation/settings/connectors");
        await Ui.ExpectVisible(page, "h1", "Connectors");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("GitHub");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Enabled");
    }

    [Fact]
    public async Task Overview_open_review_cta_navigates_to_changes()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenOverviewAsync(page);
        await Assertions.Expect(page.Locator("#content h1").First).ToContainTextAsync("Atlas");
        var openReview = page.Locator("#content .header-actions [data-route='/changes']");
        await Assertions.Expect(openReview).ToBeVisibleAsync();
        await openReview.ClickAsync(new LocatorClickOptions { Force = true });
        await Ui.ExpectVisible(page, "h1", "Pull Requests");
    }

    [Fact]
    public async Task Hash_route_deep_links_to_pipelines_and_runners()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.GotoAsync("/#/pipelines");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Build");

        await page.GotoAsync("/#/runners");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Runners");

        await page.GotoAsync("/#/settings");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Project settings");
    }

    [Fact]
    public async Task Org_and_project_settings_trees_and_legacy_aliases()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureOrgNavAsync(page);
        await Ui.NavigateAsync(page, "/organisation/settings");
        await Ui.ExpectVisible(page, "h1", "Overview");
        await Assertions.Expect(page.Locator(".settings-nav [data-route='/organisation/settings/general']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#orgName")).ToBeVisibleAsync();

        await Ui.NavigateAsync(page, "/organisation/settings/license");
        await Ui.ExpectVisible(page, "h1", "License");
        await Assertions.Expect(page.Locator("#licensingMode")).ToBeVisibleAsync();

        await page.GotoAsync("/#/licensing");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "License");

        await page.GotoAsync("/#/modules");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Modules");

        await page.GotoAsync("/#/audit");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Audit log");

        await Ui.GoToHashAsync(page, "/settings/general");
        await Ui.ExpectVisible(page, "h1", "Project settings");
        await Assertions.Expect(page.Locator(".settings-nav [data-route='/settings/general']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#projectName")).ToBeVisibleAsync();

        await Ui.NavigateAsync(page, "/settings/repositories");
        await Assertions.Expect(page.Locator("#connectRepository")).ToBeVisibleAsync();

        await Ui.NavigateAsync(page, "/settings/review");
        await Assertions.Expect(page.Locator("#minimumApprovals")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task User_menu_shows_signed_in_identity()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Assertions.Expect(page.Locator("#userMenuDropdown:not([hidden]) button[value='signout']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#userMenuDropdown")).ToContainTextAsync("Profile");
        await Assertions.Expect(page.Locator("#userHandle")).ToContainTextAsync("@maya");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#userMenuDropdown")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Seeded_sign_out_and_sign_in_round_trip()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Assertions.Expect(page.Locator("#userHandle")).ToContainTextAsync("@maya");
        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Ui.ClickAsync(page.Locator("#userMenuDropdown button[value='signout']"));
        await Ui.ExpectVisible(page, "h1", "Sign in");

        await Ui.SignInAsync(page, "maya@northstar.dev", "demo");
        await Assertions.Expect(page.Locator("#userHandle")).ToContainTextAsync("@maya");
        await Assertions.Expect(page.Locator("#projectLabel")).ToContainTextAsync("Atlas");
    }
}
