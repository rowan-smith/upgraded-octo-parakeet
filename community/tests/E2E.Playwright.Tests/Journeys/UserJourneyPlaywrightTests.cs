using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

/// <summary>
/// Broad seeded user-journey coverage: shell boundaries, settings trees, module order,
/// licence upload UX, theme, and deep links. Uses a shared host so ~100 cases stay practical.
/// </summary>
[Collection("playwright")]
public sealed class UserJourneyPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private ForgeDeckHost Host => seeded.Host;

    private async Task<PageSession> NewSessionAsync() => await Host.NewPageAsync(browser.Browser);

    // ── Org settings routes (12) ──────────────────────────────────────────

    [Theory]
    [InlineData("/organisation/settings/general", "Overview")]
    [InlineData("/organisation/settings/projects", "Projects")]
    [InlineData("/organisation/settings/users", "Users & Groups")]
    [InlineData("/organisation/settings/security", "Authentication")]
    [InlineData("/organisation/settings/permissions", "Permissions")]
    [InlineData("/organisation/settings/license", "License")]
    [InlineData("/organisation/settings/modules", "Modules")]
    [InlineData("/organisation/settings/connectors", "Connectors")]
    [InlineData("/organisation/settings/audit", "Audit log")]
    [InlineData("/organisation/settings/build", "Runners")]
    [InlineData("/organisation/settings", "Overview")]
    [InlineData("/runners", "Runners")]
    public async Task Org_settings_route_renders_heading(string route, string heading)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Ui.ExpectVisible(page, "h1", heading);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToHaveCountAsync(0);
    }

    // ── Project settings routes (6) ───────────────────────────────────────

    [Theory]
    [InlineData("/settings/general", "Project settings")]
    [InlineData("/settings/members", "Project settings")]
    [InlineData("/settings/modules", "Project settings")]
    [InlineData("/settings/repositories", "Project settings")]
    [InlineData("/settings/review", "Project settings")]
    [InlineData("/settings", "Project settings")]
    public async Task Project_settings_route_renders_shell(string route, string heading)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Ui.ExpectVisible(page, "h1", heading);
        await Assertions.Expect(page.Locator(".settings-nav")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToHaveCountAsync(0);
    }

    // ── Project module routes (14) ────────────────────────────────────────

    [Theory]
    [InlineData("/overview")]
    [InlineData("/files")]
    [InlineData("/commits")]
    [InlineData("/source-branches")]
    [InlineData("/tags")]
    [InlineData("/changes")]
    [InlineData("/queue")]
    [InlineData("/pipelines")]
    [InlineData("/runs")]
    [InlineData("/jobs")]
    [InlineData("/tests")]
    [InlineData("/artifacts")]
    [InlineData("/environments")]
    [InlineData("/deployments")]
    public async Task Project_module_route_keeps_project_shell(string route)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/people']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/home']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/audit']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/modules']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".sidebar-footer")).ToHaveCountAsync(0);
    }

    // ── Legacy aliases (8) ────────────────────────────────────────────────

    [Theory]
    [InlineData("/licensing", "License")]
    [InlineData("/organisation/licensing", "License")]
    [InlineData("/modules", "Modules")]
    [InlineData("/connectors", "Connectors")]
    [InlineData("/audit", "Audit log")]
    [InlineData("/runners", "Runners")]
    [InlineData("/organisation/settings", "Overview")]
    [InlineData("/settings", "Project settings")]
    public async Task Legacy_alias_lands_on_expected_page(string route, string heading)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.GoToHashAsync(page, route);
        await Ui.ExpectVisible(page, "h1", heading);
    }

    // ── Org / project shell boundaries (12) ───────────────────────────────

    [Theory]
    [InlineData("/home")]
    [InlineData("/projects")]
    [InlineData("/people")]
    [InlineData("/organisation/settings/general")]
    [InlineData("/organisation/settings/modules")]
    [InlineData("/organisation/settings/build")]
    public async Task Org_shell_hides_project_module_nav(string route)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/home']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/overview", "/organisation/settings")]
    [InlineData("/changes", "/organisation/settings")]
    [InlineData("/pipelines", "/people")]
    [InlineData("/settings/general", "/organisation/settings")]
    [InlineData("/settings/members", "/modules")]
    [InlineData("/settings/modules", "/audit")]
    public async Task Project_shell_excludes_org_only_primary_routes(string projectRoute, string forbidden)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, projectRoute);
        await Assertions.Expect(page.Locator($"#primaryNav [data-route='{forbidden}']")).ToHaveCountAsync(0);
    }

    // ── Settings nav trees (16) ───────────────────────────────────────────

    [Theory]
    [InlineData("/organisation/settings/general", "/organisation/settings/general")]
    [InlineData("/organisation/settings/projects", "/organisation/settings/projects")]
    [InlineData("/organisation/settings/users", "/organisation/settings/users")]
    [InlineData("/organisation/settings/security", "/organisation/settings/security")]
    [InlineData("/organisation/settings/permissions", "/organisation/settings/permissions")]
    [InlineData("/organisation/settings/license", "/organisation/settings/license")]
    [InlineData("/organisation/settings/modules", "/organisation/settings/modules")]
    [InlineData("/organisation/settings/connectors", "/organisation/settings/connectors")]
    [InlineData("/organisation/settings/audit", "/organisation/settings/audit")]
    [InlineData("/organisation/settings/build", "/organisation/settings/build")]
    public async Task Org_settings_nav_marks_active_item(string route, string active)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator($".settings-nav-item.active[data-route='{active}']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/runners")]
    [InlineData("/organisation/settings/build")]
    public async Task Runner_entry_points_converge_on_org_settings(string route)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.GoToHashAsync(page, route);
        await Ui.ExpectVisible(page, "h1", "Runners");
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Project_settings_build_redirects_to_organisation_runners()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.EvaluateAsync("() => { location.hash = '#/settings/build'; }");
        await page.WaitForFunctionAsync("() => (location.hash || '').includes('/organisation/settings/build')");
        await Ui.WaitForAppIdleAsync(page);
        await Ui.ExpectVisible(page, "h1", "Runners");
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".settings-nav-item.active[data-route='/organisation/settings/build']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/settings/general", "/settings/general")]
    [InlineData("/settings/members", "/settings/members")]
    [InlineData("/settings/modules", "/settings/modules")]
    [InlineData("/settings/repositories", "/settings/repositories")]
    [InlineData("/settings/review", "/settings/review")]
    public async Task Project_settings_nav_or_redirect_behaves(string route, string expectedActiveOrLanding)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, route);
        await Assertions.Expect(page.Locator($".settings-nav-item.active[data-route='{expectedActiveOrLanding}']")).ToBeVisibleAsync();
    }

    // ── Module / nav order ────────────────────────────────────────────────

    [Fact]
    public async Task Project_nav_group_order_is_code_review_build_deploy()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        var groups = await page.Locator("#primaryNav .nav-group").AllTextContentsAsync();
        var moduleGroups = groups
            .Select(g => g.Trim())
            .Where(g => g is "Code" or "Review" or "Build" or "Deploy")
            .ToList();
        Assert.Equal(new[] { "Code", "Review", "Build", "Deploy" }, moduleGroups);
    }

    [Fact]
    public async Task Project_nav_has_expected_module_entries_and_no_runners()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        foreach (var route in new[]
                 {
                     "/files", "/commits", "/source-branches", "/tags",
                     "/changes", "/queue",
                     "/pipelines", "/runs", "/jobs", "/tests", "/artifacts",
                     "/environments", "/deployments",
                     "/settings/members", "/settings", "/home"
                 })
        {
            await Assertions.Expect(page.Locator($"#primaryNav [data-route='{route}']")).ToBeVisibleAsync();
        }

        await Assertions.Expect(page.Locator("#primaryNav [data-route='/runners']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/people']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/audit']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/modules']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Modules_catalogue_lists_core_and_hides_commercial_alias()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenModulesAsync(page);
        var names = await page.Locator("#modulesList .module-card h3").AllTextContentsAsync();
        var installed = names.Select(n => n.Split(' ', 2)[0].Trim()).ToList();
        Assert.Contains("Code", installed);
        Assert.Contains("Review", installed);
        Assert.Contains("Build", installed);

        await Assertions.Expect(page.Locator("[data-extension-id='forgedeck.review.commercial']")).ToHaveCountAsync(0);

        var team = page.Locator("[data-extension-id='forgedeck.review.team']");
        if (await team.CountAsync() > 0)
        {
            await Assertions.Expect(team).ToHaveAttributeAsync("data-edition", "Team");
            await Assertions.Expect(team.Locator("[data-ext-install]")).ToHaveCountAsync(0);
        }
    }

    // ── Licence file upload UX ────────────────────────────────────────────

    [Fact]
    public async Task Licence_settings_exposes_file_input_not_paste_textarea()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingFile")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#licensingFile")).ToHaveAttributeAsync("type", "file");
        await Assertions.Expect(page.Locator("textarea#licensingPayload")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("input#licensingPayload[type='hidden']")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#licensingInstall")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Licence_file_upload_rejects_invalid_json_file()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenLicensingAsync(page);
        var path = Path.Combine(Path.GetTempPath(), $"bad-licence-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ \"not\": \"a licence\" }");
        try
        {
            await page.Locator("#licensingFile").SetInputFilesAsync(path);
            await page.Locator("#licensingInstall").ClickAsync();
            await Ui.ExpectToastAsync(page, "Licence could not be validated");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Theme / topbar ────────────────────────────────────────────────────

    [Fact]
    public async Task Dark_theme_topbar_is_not_opaque_white()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await page.EvaluateAsync("() => typeof applyTheme === 'function' && applyTheme('dark')");
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'dark'");
        var bg = await page.Locator(".topbar").EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        // Must not be opaque white (the prior dark-theme bug).
        Assert.False(bg is "rgb(255, 255, 255)" or "rgba(255, 255, 255, 1)" or "rgba(255, 255, 255, 0.98)");
        var theme = await page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')");
        Assert.Equal("dark", theme);
    }

    [Fact]
    public async Task Light_theme_topbar_uses_panel_surface()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await page.EvaluateAsync("() => typeof applyTheme === 'function' && applyTheme('light')");
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') !== 'dark'");
        await Assertions.Expect(page.Locator(".topbar")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Theme_preference_round_trips_dark_and_light()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
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
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'dark'");
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
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') !== 'dark'");
    }

    // ── User menu settings routing ────────────────────────────────────────

    [Fact]
    public async Task User_menu_settings_from_org_opens_organisation_settings()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/home");
        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Assertions.Expect(page.Locator("#userMenuDropdown:not([hidden]) [data-user-action='settings']")).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator("[data-user-action='settings']"));
        await Ui.ExpectVisible(page, "h1", "Overview");
        await Assertions.Expect(page.Locator(".settings-nav [data-route='/organisation/settings/general']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task User_menu_settings_from_project_opens_project_settings()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        await Ui.ClickAsync(page.Locator("#userMenu"));
        await Assertions.Expect(page.Locator("#userMenuDropdown:not([hidden]) [data-user-action='settings']")).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator("[data-user-action='settings']"));
        await Ui.ExpectVisible(page, "h1", "Project settings");
        await Assertions.Expect(page.Locator(".settings-nav [data-route='/settings/general']")).ToBeVisibleAsync();
    }

    // ── Overview / brand / context ────────────────────────────────────────

    [Theory]
    [InlineData("/settings/members")]
    [InlineData("/runs")]
    [InlineData("/files")]
    public async Task Overview_action_links_stay_in_project_context(string expectedRoute)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenOverviewAsync(page);
        var link = page.Locator($"#content [data-route='{expectedRoute}']").First;
        await Assertions.Expect(link).ToBeVisibleAsync();
        await link.ClickAsync();
        await Ui.WaitForAppIdleAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Overview_does_not_link_to_org_people_or_audit()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenOverviewAsync(page);
        await Assertions.Expect(page.Locator("#content [data-route='/people']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#content [data-route='/audit']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#content [data-route='/modules']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Brand_mark_returns_to_organisation_home()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/changes");
        await page.Locator(".topbar-brand").ClickAsync();
        await Ui.WaitForAppIdleAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/home']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Organisation_home_via_brand_switches_shell_from_project()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureProjectNavAsync(page);
        await page.Locator(".topbar-brand").ClickAsync();
        await Ui.WaitForAppIdleAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/projects']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToHaveCountAsync(0);
    }

    // ── Runners org ownership ─────────────────────────────────────────────

    [Fact]
    public async Task Runners_page_is_organisation_settings_shell()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenRunnersAsync(page);
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#refreshRunners")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".settings-nav-item.active[data-route='/organisation/settings/build']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("organisation");
    }

    // ── Shell chrome matrix (10) ──────────────────────────────────────────

    [Theory]
    [InlineData("/home", true)]
    [InlineData("/projects", true)]
    [InlineData("/people", true)]
    [InlineData("/organisation/settings/license", true)]
    [InlineData("/overview", false)]
    [InlineData("/changes", false)]
    [InlineData("/pipelines", false)]
    [InlineData("/settings/general", false)]
    [InlineData("/files", false)]
    [InlineData("/queue", false)]
    public async Task Shell_shows_correct_context_markers(string route, bool orgShell)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        if (orgShell)
        {
            await Ui.EnsureOrgNavAsync(page);
        }
        else
        {
            await Ui.EnsureProjectNavAsync(page);
        }

        await Ui.GoToHashAsync(page, route);
        if (orgShell)
        {
            await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToHaveCountAsync(0);
        }
        else
        {
            await Assertions.Expect(page.Locator("#primaryNav [data-route='/overview']")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToHaveCountAsync(0);
        }
    }

    // ── End-to-end walk ───────────────────────────────────────────────────

    [Fact]
    public async Task Seeded_operator_walks_org_then_project_then_back()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;

        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/home");
        await Assertions.Expect(page.Locator("#content")).ToBeVisibleAsync();

        await Ui.GoToHashAsync(page, "/projects");
        await Ui.ExpectVisible(page, "h1", "Projects");

        await Ui.GoToHashAsync(page, "/organisation/settings/modules");
        await Ui.ExpectVisible(page, "h1", "Modules");

        await Ui.GoToHashAsync(page, "/organisation/settings/build");
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();

        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/overview");
        await Assertions.Expect(page.Locator("#primaryNav .nav-group").Filter(new LocatorFilterOptions { HasTextString = "Code" })).ToBeVisibleAsync();

        await Ui.GoToHashAsync(page, "/changes");
        await Ui.ExpectVisible(page, "h1", "Pull Requests");

        await Ui.GoToHashAsync(page, "/pipelines");
        await Ui.ExpectVisible(page, "h1", "Build");

        await Ui.GoToHashAsync(page, "/settings/members");
        await Ui.ExpectVisible(page, "h1", "Project settings");

        await page.Locator(".topbar-brand").ClickAsync();
        await Ui.WaitForAppIdleAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/organisation/settings']")).ToBeVisibleAsync();

        var modules = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch('/api/platform/modules', {
                headers: { Authorization: `Bearer ${token}` }
              });
              return response.json();
            }
            """);
        Assert.True(modules.ValueKind == JsonValueKind.Object);
        Assert.True(modules.TryGetProperty("modules", out _));
    }
}
