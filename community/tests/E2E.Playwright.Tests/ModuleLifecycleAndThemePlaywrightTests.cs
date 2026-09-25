using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

[Collection("playwright")]
public sealed class ModuleLifecyclePlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Org_disable_hides_module_from_project_navigation()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Module Org", email: "mod@example.com", username: "modowner", installDefaultModules: false);
        await Ui.InstallBundledModulesAsync(page, "forgedeck.code", "forgedeck.review", "forgedeck.build");

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToBeVisibleAsync();

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              const response = await fetch('/api/platform/extensions/forgedeck.review/disable', { method: 'POST', headers, body: '{}' });
              if (!response.ok) throw new Error(await response.text());
              if (typeof reloadComposition === 'function') await reloadComposition();
            }
            """);

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Org_reenable_restores_module_navigation()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Reenable Org", email: "re@example.com", username: "reowner", installDefaultModules: false);
        await Ui.InstallBundledModulesAsync(page, "forgedeck.code", "forgedeck.review");

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              await fetch('/api/platform/extensions/forgedeck.code/disable', { method: 'POST', headers, body: '{}' });
              if (typeof reloadComposition === 'function') await reloadComposition();
            }
            """);
        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToHaveCountAsync(0);

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              await fetch('/api/platform/extensions/forgedeck.code/enable', { method: 'POST', headers, body: '{}' });
              if (typeof reloadComposition === 'function') await reloadComposition();
            }
            """);
        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Project_disable_hides_module_only_for_that_project()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Project Module Org", email: "pm@example.com", username: "pmowner", installDefaultModules: false);
        await Ui.InstallBundledModulesAsync(page, "forgedeck.code", "forgedeck.review", "forgedeck.build");

        var projectId = await page.EvaluateAsync<string>("() => state.context?.project?.id");
        Assert.False(string.IsNullOrWhiteSpace(projectId));

        await page.EvaluateAsync("""
            async (projectId) => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              const response = await fetch(`/api/projects/${projectId}/modules`, {
                method: 'PUT',
                headers,
                body: JSON.stringify({ enabledExtensionIds: ['forgedeck.code', 'forgedeck.build'] })
              });
              if (!response.ok) throw new Error(await response.text());
              if (typeof reloadComposition === 'function') await reloadComposition();
              if (typeof renderNavigation === 'function') renderNavigation();
            }
            """, projectId);

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToHaveCountAsync(0);

        await Ui.GoToHashAsync(page, "/settings/modules");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Project modules");
        await Assertions.Expect(page.Locator("[data-project-module-toggle='forgedeck.review']")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("[data-project-module-toggle='forgedeck.code']")).ToBeCheckedAsync();
    }

    [Fact]
    public async Task Project_cannot_enable_org_disabled_module()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Align Org", email: "align@example.com", username: "alignowner", installDefaultModules: false);
        await Ui.InstallBundledModulesAsync(page, "forgedeck.code", "forgedeck.review");

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              await fetch('/api/platform/extensions/forgedeck.review/disable', { method: 'POST', headers, body: '{}' });
            }
            """);

        var projectId = await page.EvaluateAsync<string>("() => state.context?.project?.id");
        var result = await page.EvaluateAsync<JsonElement>("""
            async (projectId) => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              const response = await fetch(`/api/projects/${projectId}/modules`, {
                method: 'PUT',
                headers,
                body: JSON.stringify({ enabledExtensionIds: ['forgedeck.code', 'forgedeck.review'] })
              });
              const body = await response.json();
              return { status: response.status, body };
            }
            """, projectId);

        Assert.Equal(200, result.GetProperty("status").GetInt32());
        var modules = result.GetProperty("body").EnumerateArray().ToArray();
        var review = modules.First(m => m.GetProperty("extensionId").GetString() == "forgedeck.review");
        Assert.False(review.GetProperty("organisationEnabled").GetBoolean());
        Assert.False(review.GetProperty("enabled").GetBoolean());
    }
}

[Collection("playwright")]
public sealed class CodeReviewNavigationPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Code_file_explorer_and_source_routes_are_reachable()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/files");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Repositories");
        await Assertions.Expect(page.Locator("#content")).ToBeVisibleAsync();

        await Ui.GoToHashAsync(page, "/commits");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Commits");

        await Ui.GoToHashAsync(page, "/source-branches");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Branches");

        await Ui.GoToHashAsync(page, "/tags");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Tags");
    }

    [Fact]
    public async Task Review_changes_and_queue_expose_module_features()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenChangesAsync(page);
        await Assertions.Expect(page.Locator("#discoverButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#importButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#statusFilter")).ToBeVisibleAsync();

        await Ui.OpenQueueAsync(page);
        await Assertions.Expect(page.Locator("#content h1")).ToContainTextAsync("Review queue");
    }

    [Fact]
    public async Task Disabling_code_at_project_level_blocks_files_route()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Code Off Org", email: "codeoff@example.com", username: "codeoff", installDefaultModules: false);
        await Ui.InstallBundledModulesAsync(page, "forgedeck.code", "forgedeck.review");

        var projectId = await page.EvaluateAsync<string>("() => state.context?.project?.id");
        await page.EvaluateAsync("""
            async (projectId) => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
              await fetch(`/api/projects/${projectId}/modules`, {
                method: 'PUT', headers,
                body: JSON.stringify({ enabledExtensionIds: ['forgedeck.review'] })
              });
              if (typeof reloadComposition === 'function') await reloadComposition();
            }
            """, projectId);

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/files']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToBeVisibleAsync();

        await page.EvaluateAsync("() => location.hash = '#/files'");
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Code module is not enabled");
    }
}

[Collection("playwright")]
public sealed class ThemePreferencesPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Theme_preference_persists_on_user_profile()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Theme Org", email: "theme@example.com", username: "themeowner", installDefaultModules: false);

        await page.Locator("#userMenu").ClickAsync();
        await page.Locator("[data-user-action='theme']").ClickAsync();
        await Ui.ExpectModalOpenAsync(page, "Theme");
        await page.Locator("input[name='themeChoice'][value='dark']").CheckAsync();
        await page.Locator("#modal[open] button[value='submit']").ClickAsync();
        await Ui.ExpectToastAsync(page, "Theme set to dark");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        var me = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch('/api/users/me', { headers: { Authorization: `Bearer ${token}` } });
              return await response.json();
            }
            """);
        Assert.Equal("dark", me.GetProperty("profile").GetProperty("theme").GetString());

        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('app')?.getAttribute('aria-busy') === 'false'");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
    }

    [Fact]
    public async Task Light_theme_can_be_restored()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              await fetch('/api/users/me/preferences', {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ theme: 'dark' })
              });
              if (typeof applyTheme === 'function') applyTheme('dark');
            }
            """);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        await page.EvaluateAsync("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              await fetch('/api/users/me/preferences', {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ theme: 'light' })
              });
              if (typeof applyTheme === 'function') applyTheme('light');
            }
            """);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
    }
}

[Collection("playwright")]
public sealed class SetupAuthPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Stale_local_token_does_not_skip_bootstrap_login()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await page.EvaluateAsync("() => localStorage.setItem('forgedeck.token', 'not-a-valid-session')");
        await page.ReloadAsync();
        await Ui.ExpectVisible(page, "h1", "Initial Setup");
        await Assertions.Expect(page.Locator("#bootstrapSignIn")).ToBeVisibleAsync();

        await page.Locator("#bootstrapSignIn").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Set up your organisation");
        await page.Locator("#setupOrgContinue").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Choose your licence");
    }
}
