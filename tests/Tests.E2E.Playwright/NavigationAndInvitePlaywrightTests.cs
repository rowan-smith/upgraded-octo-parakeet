using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class SeededNavigationPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Seeded_host_auto_signs_in_and_shows_shell()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Assertions.Expect(page.Locator("#orgLabel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#projectLabel")).ToContainTextAsync("Atlas");
        await Assertions.Expect(page.Locator("#userHandle")).ToContainTextAsync("@maya");
        await Assertions.Expect(page.Locator("#primaryNav")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Module_navigation_includes_review_and_pipelines()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/changes']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/pipelines']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/runs']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav .nav-group").Filter(new LocatorFilterOptions { HasTextString = "Build" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/tags']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#primaryNav [data-route='/repositories']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Navigating_to_changes_and_pipelines_updates_content()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#primaryNav [data-route='/changes']").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Pull Requests");
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Pull Requests");

        await page.Locator("#primaryNav [data-route='/pipelines']").ClickAsync();
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Pipelines");
        await Ui.ExpectVisible(page, "h1", "Build");
    }

    [Fact]
    public async Task People_audit_and_modules_routes_render()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenPeopleAsync(page);
        await Ui.ExpectVisible(page, "h1", "People");

        await Ui.OpenAuditAsync(page);
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Audit");

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Modules");
    }

    [Fact]
    public async Task Org_home_and_projects_routes_render()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureOrgNavAsync(page);
        await page.Locator("#primaryNav [data-route='/home']").ClickAsync();
        await Assertions.Expect(page.Locator("#content h1").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".home-metrics")).ToBeVisibleAsync();

        await page.Locator("#primaryNav [data-route='/projects']").ClickAsync();
        await Ui.ExpectVisible(page, "h1", "Projects");
        await Assertions.Expect(page.Locator(".projects-grid")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Code_files_commits_and_branches_routes_render()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.EnsureProjectNavAsync(page);
        await page.Locator("#primaryNav [data-route='/files']").ClickAsync();
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Repositories");
        await Assertions.Expect(page.Locator(".file-browser").Or(page.Locator("#content h1")).First).ToBeVisibleAsync();

        await page.Locator("#primaryNav [data-route='/commits']").ClickAsync();
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Commits");

        await page.Locator("#primaryNav [data-route='/source-branches']").ClickAsync();
        await Assertions.Expect(page.Locator("#breadcrumbs")).ToContainTextAsync("Branches");
    }

    [Fact]
    public async Task Overview_shows_organisation_and_project_context()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenOverviewAsync(page);
        await Assertions.Expect(page.Locator("#content h1").First).ToContainTextAsync("Atlas");
        await Assertions.Expect(page.Locator("#content")).ToContainTextAsync("Northstar");
    }
}

[Collection("playwright")]
public sealed class InviteAcceptPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Invitation_accept_page_joins_organisation()
    {
        await using var host = ForgeDeckHost.StartEmpty();

        using var setup = await host.Api.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Invite Org",
            displayName = "Invite Owner",
            username = "inviteowner",
            email = "invite-owner-api@example.com",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var setupDoc = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        var ownerToken = setupDoc.RootElement.GetProperty("token").GetString()!;

        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, "/api/organisation/invitations")
        {
            Content = JsonContent.Create(new { email = "invited-user@example.com", role = "Member" })
        };
        inviteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var invite = await host.Api.SendAsync(inviteRequest);
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var token = inviteDoc.RootElement.GetProperty("token").GetString()!;

        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;
        await page.GotoAsync($"/#/invite/{token}");
        await page.WaitForFunctionAsync("() => document.getElementById('app')?.getAttribute('aria-busy') === 'false'");
        await Ui.ExpectVisible(page, "h1", "Join organisation");

        await page.Locator("#inviteDisplayName").FillAsync("Invited User");
        await page.Locator("#inviteUsername").FillAsync("inviteduser");
        await page.Locator("#invitePassword").FillAsync("password123");
        await page.Locator("#inviteAccept").ClickAsync();

        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await Assertions.Expect(page.Locator("#userHandle")).ToHaveTextAsync("@inviteduser");
    }
}
