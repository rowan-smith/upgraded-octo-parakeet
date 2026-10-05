using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Playwright.Tests.Journeys;

/// <summary>Permission settings journeys plus additive custom-role flows on a shared seeded host.</summary>
[Collection("playwright")]
public sealed class AccessControlPlaywrightTests(PlaywrightBrowserFixture browser, SeededHostFixture seeded)
    : IClassFixture<SeededHostFixture>
{
    private async Task<PageSession> NewSessionAsync() => await seeded.Host.NewPageAsync(browser.Browser);

    [Fact]
    public async Task Permissions_page_renders_roles_and_catalogue()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Assertions.Expect(page.Locator("#content h1")).ToContainTextAsync("Permissions");
        await Assertions.Expect(page.Locator("#accessRolesList")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#createAccessRole")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".perm-grid").First).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Member")]
    [InlineData("Viewer")]
    [InlineData("Developer")]
    [InlineData("Reviewer")]
    [InlineData("Builder")]
    [InlineData("Deployer")]
    public async Task Built_in_role_is_listed(string roleName)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Assertions.Expect(page.Locator("#accessRolesList .module-card").Filter(new() { HasText = roleName }).First)
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Every_built_in_role_is_marked_as_a_system_role()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        Assert.True(await page.Locator("[data-role-system='1']").CountAsync() >= 8);
        await Assertions.Expect(page.Locator("[data-role-system='1'] .pill").First).ToContainTextAsync("System");
    }

    [Fact]
    public async Task System_roles_cannot_be_edited_or_deleted_from_the_list()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Assertions.Expect(page.Locator("[data-role-system='1'] [data-edit-role]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-role-system='1'] [data-delete-role]")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("organisation.read")]
    [InlineData("organisation.manage")]
    [InlineData("organisation.destroy")]
    [InlineData("users.read")]
    [InlineData("users.manage")]
    [InlineData("teams.manage")]
    [InlineData("projects.create")]
    [InlineData("projects.manage")]
    [InlineData("audit.read")]
    [InlineData("licensing.manage")]
    [InlineData("source.repository.read")]
    [InlineData("review.read")]
    [InlineData("review.approve")]
    [InlineData("review.merge")]
    [InlineData("git.repository.push")]
    [InlineData("build.read")]
    [InlineData("build.run")]
    [InlineData("build.manage")]
    [InlineData("build.runner.manage")]
    [InlineData("deploy.read")]
    [InlineData("deploy.execute")]
    [InlineData("deploy.manage")]
    public async Task Permission_catalogue_lists_key(string key)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Assertions.Expect(page.Locator($".perm-chip code:text-is('{key}')")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Permission_catalogue_is_grouped_into_categories()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        Assert.True(await page.Locator(".perm-grid").CountAsync() > 1);
        Assert.True(await page.Locator(".perm-chip").CountAsync() >= 32);
    }

    [Fact]
    public async Task Role_editor_opens_with_every_permission_unchecked()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Ui.ClickAsync(page.Locator("#createAccessRole"));

        await Ui.ExpectModalOpenAsync(page, "New custom role");
        await Assertions.Expect(page.Locator("#roleName")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#roleSlug")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator(".role-perm-editor")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-role-perm]:checked")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Role_editor_lists_every_catalogue_permission_as_a_choice()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Ui.ClickAsync(page.Locator("#createAccessRole"));

        await Assertions.Expect(page.Locator("[data-role-perm]")).ToHaveCountAsync(63);
    }

    [Fact]
    public async Task Custom_role_can_be_created_and_appears_in_the_list()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await CreateRoleAsync(page, $"Release Captain {suffix}", $"release-captain-{suffix}", "build.run", "deploy.execute");

        await Ui.ExpectToastAsync(page, "Role saved");
        var card = page.Locator("#accessRolesList .module-card").Filter(new() { HasText = $"Release Captain {suffix}" });
        await Assertions.Expect(card).ToHaveCountAsync(1);
        await Assertions.Expect(card).ToContainTextAsync("2 permissions");
    }

    [Fact]
    public async Task Custom_role_exposes_edit_and_delete_actions()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);
        await CreateRoleAsync(page, $"Auditor {suffix}", $"auditor-{suffix}", "audit.read");
        await Ui.ExpectToastAsync(page, "Role saved");

        var card = page.Locator("[data-role-system='0']").Filter(new() { HasText = $"Auditor {suffix}" });
        await Assertions.Expect(card.Locator("[data-edit-role]")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("[data-delete-role]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Custom_role_can_be_created_without_any_permissions()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await CreateRoleAsync(page, $"Shell Role {suffix}", $"shell-role-{suffix}");

        await Ui.ExpectToastAsync(page, "Role saved");
        await Assertions.Expect(page.Locator("#accessRolesList .module-card").Filter(new() { HasText = $"Shell Role {suffix}" }))
            .ToContainTextAsync("0 permissions");
    }

    [Fact]
    public async Task Custom_role_editor_preselects_the_saved_permissions()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);
        await CreateRoleAsync(page, $"Preselect {suffix}", $"preselect-{suffix}", "build.read", "build.run", "deploy.read");
        await Ui.ExpectToastAsync(page, "Role saved");

        await Ui.ClickAsync(page.Locator("#accessRolesList .module-card")
            .Filter(new() { HasText = $"Preselect {suffix}" })
            .Locator("[data-edit-role]"));

        await Ui.ExpectModalOpenAsync(page, "Edit role");
        await Assertions.Expect(page.Locator("[data-role-perm]:checked")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#roleSlug")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Custom_role_can_be_renamed_and_repermissioned()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);
        await CreateRoleAsync(page, $"Before {suffix}", $"before-{suffix}", "build.read");
        await Ui.ExpectToastAsync(page, "Role saved");

        await Ui.ClickAsync(page.Locator("#accessRolesList .module-card")
            .Filter(new() { HasText = $"Before {suffix}" })
            .Locator("[data-edit-role]"));
        await page.Locator("#roleName").FillAsync($"After {suffix}");
        await Ui.ClickAsync(page.Locator("[data-role-perm='build.cancel']"));
        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));

        await Ui.ExpectToastAsync(page, "Role saved");
        var card = page.Locator("#accessRolesList .module-card").Filter(new() { HasText = $"After {suffix}" });
        await Assertions.Expect(card).ToHaveCountAsync(1);
        await Assertions.Expect(card).ToContainTextAsync("2 permissions");
    }

    [Fact]
    public async Task Custom_role_can_be_deleted()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await OpenPermissionsAsync(page);
        await CreateRoleAsync(page, $"Temporary {suffix}", $"temporary-{suffix}", "audit.read");
        await Ui.ExpectToastAsync(page, "Role saved");

        await Ui.ClickAsync(page.Locator("#accessRolesList .module-card")
            .Filter(new() { HasText = $"Temporary {suffix}" })
            .Locator("[data-delete-role]"));

        await Ui.ExpectToastAsync(page, "Role deleted");
        await Assertions.Expect(page.Locator("#accessRolesList .module-card").Filter(new() { HasText = $"Temporary {suffix}" }))
            .ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("viewer")]
    [InlineData("developer")]
    [InlineData("reviewer")]
    [InlineData("builder")]
    [InlineData("deployer")]
    public async Task Custom_role_cannot_reuse_a_built_in_slug(string slug)
    {
        var name = $"Clash {Suffix()}";
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await CreateRoleAsync(page, name, slug, "audit.read");

        await Assertions.Expect(page.Locator("#toast.error")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Ui.CloseModalAsync(page);
        await OpenPermissionsAsync(page);
        await Assertions.Expect(page.Locator("#accessRolesList .module-card").Filter(new() { HasText = name }))
            .ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("review.publish")]
    public async Task Role_editor_never_offers_an_unknown_permission(string permission)
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await Ui.ClickAsync(page.Locator("#createAccessRole"));

        await Assertions.Expect(page.Locator($"[data-role-perm='{permission}']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Custom_role_requires_a_name()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenPermissionsAsync(page);

        await CreateRoleAsync(page, string.Empty, $"nameless-{Suffix()}", "audit.read");

        await Assertions.Expect(page.Locator("#toast.error")).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Fact]
    public async Task Team_creation_offers_an_access_role_dropdown()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenTeamsAsync(page);

        await Ui.ClickAsync(page.Locator("#createTeam"));

        await Ui.ExpectModalOpenAsync(page, "Create team");
        await Assertions.Expect(page.Locator("#teamRole")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#teamRole option").CountAsync() >= 9);
        Assert.Equal("", await page.Locator("#teamRole").InputValueAsync());
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Developer")]
    [InlineData("Builder")]
    [InlineData("Deployer")]
    [InlineData("Reviewer")]
    [InlineData("Member")]
    public async Task Team_can_be_created_with_a_built_in_role(string roleName)
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenTeamsAsync(page);

        await Ui.ClickAsync(page.Locator("#createTeam"));
        await page.Locator("#teamName").FillAsync($"{roleName} Crew {suffix}");
        await page.Locator("#teamSlug").FillAsync($"{roleName.ToLowerInvariant()}-crew-{suffix}");
        await page.Locator("#teamRole").SelectOptionAsync(new SelectOptionValue { Label = roleName });
        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));

        await Ui.ExpectToastAsync(page, "Team created");
        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"{roleName} Crew {suffix}" }))
            .ToContainTextAsync($"role: {roleName}");
    }

    [Fact]
    public async Task Team_created_without_a_role_shows_as_inheriting_organisation_access()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenTeamsAsync(page);

        await Ui.ClickAsync(page.Locator("#createTeam"));
        await page.Locator("#teamName").FillAsync($"Plain Crew {suffix}");
        await page.Locator("#teamSlug").FillAsync($"plain-crew-{suffix}");
        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));

        await Ui.ExpectToastAsync(page, "Team created");
        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"Plain Crew {suffix}" }))
            .ToContainTextAsync("no role");
    }

    [Fact]
    public async Task Team_role_can_be_changed_from_the_team_detail_settings()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Switchers {suffix}", $"switchers-{suffix}");

        await OpenTeamSettingsAsync(page, $"Switchers {suffix}");
        await page.Locator("#teamDetailRole").SelectOptionAsync(new SelectOptionValue { Label = "Reviewer" });
        await Ui.ClickAsync(page.Locator("#teamRoleSave"));

        await Ui.ExpectToastAsync(page, "Team role updated");
        await OpenTeamsAsync(page);
        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"Switchers {suffix}" }))
            .ToContainTextAsync("role: Reviewer");
    }

    [Fact]
    public async Task Team_role_can_be_cleared_from_the_team_detail_settings()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Clearers {suffix}", $"clearers-{suffix}", "Builder");

        await OpenTeamSettingsAsync(page, $"Clearers {suffix}");
        await page.Locator("#teamDetailRole").SelectOptionAsync(new SelectOptionValue { Value = "" });
        await Ui.ClickAsync(page.Locator("#teamRoleSave"));

        await Ui.ExpectToastAsync(page, "Team role updated");
        await OpenTeamsAsync(page);
        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"Clearers {suffix}" }))
            .ToContainTextAsync("no role");
    }

    [Fact]
    public async Task Team_detail_settings_preselects_the_current_role()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Preselected {suffix}", $"preselected-{suffix}", "Deployer");

        await OpenTeamSettingsAsync(page, $"Preselected {suffix}");

        await Assertions.Expect(page.Locator("#teamDetailRole")).ToBeVisibleAsync();
        var selected = await page.Locator("#teamDetailRole option:checked").InnerTextAsync();
        Assert.Equal("Deployer", selected.Trim());
    }

    [Fact]
    public async Task Organisation_users_settings_shows_the_role_attached_to_a_team()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Settings Crew {suffix}", $"settings-crew-{suffix}", "Viewer");

        await Ui.GoToHashAsync(page, "/organisation/settings/users");
        await Ui.ClickAsync(page.Locator("#peopleTeams"));

        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"Settings Crew {suffix}" }))
            .ToContainTextAsync("role: Viewer");
    }

    [Fact]
    public async Task Project_member_grant_offers_an_access_role_dropdown()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenProjectMembersAsync(page);

        await Ui.ClickAsync(page.Locator("#addProjectMember"));

        await Ui.ExpectModalOpenAsync(page, "Add project member");
        await Assertions.Expect(page.Locator("#projectMemberRole")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#projectMemberRole option").CountAsync() >= 9);
        Assert.Equal("", await page.Locator("#projectMemberRole").InputValueAsync());
    }

    [Fact]
    public async Task Project_team_grant_offers_an_access_role_dropdown()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await OpenProjectTeamsAccessAsync(page);

        await Ui.ClickAsync(page.Locator("#addProjectTeam"));

        await Ui.ExpectModalOpenAsync(page, "Add project team");
        await Assertions.Expect(page.Locator("#projectTeamRole")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#modal[open]")).ToContainTextAsync("fall back to the role attached to the team");
    }

    [Fact]
    public async Task Project_team_can_be_granted_with_a_role()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Grantees {suffix}", $"grantees-{suffix}");
        await OpenProjectTeamsAccessAsync(page);

        await Ui.ClickAsync(page.Locator("#addProjectTeam"));
        await page.Locator("#projectTeamId").SelectOptionAsync(new SelectOptionValue { Label = $"Grantees {suffix}" });
        await page.Locator("#projectTeamRole").SelectOptionAsync(new SelectOptionValue { Label = "Builder" });
        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));

        await Ui.ExpectToastAsync(page, "Team added");
        await Assertions.Expect(page.Locator(".module-card").Filter(new() { HasText = $"Grantees {suffix}" }))
            .ToContainTextAsync("role: Builder");
    }

    [Fact]
    public async Task Permission_catalogue_api_describes_every_permission()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);

        var catalogue = await GetJsonAsync(page, "/api/access/permissions");

        Assert.Equal(200, catalogue.GetProperty("status").GetInt32());
        var entries = catalogue.GetProperty("body");
        Assert.True(entries.GetArrayLength() >= 32);
        Assert.All(entries.EnumerateArray(), entry =>
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("category").GetString())));
    }

    [Fact]
    public async Task Role_api_returns_the_built_in_system_roles()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);

        var roles = await GetJsonAsync(page, "/api/access/roles");

        Assert.Equal(200, roles.GetProperty("status").GetInt32());
        Assert.True(roles.GetProperty("body").EnumerateArray().Count(role => role.GetProperty("isSystem").GetBoolean()) >= 8);
    }

    [Fact]
    public async Task Effective_permissions_api_reports_the_owner_ceiling()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);

        var effective = await GetJsonAsync(page, "/api/access/effective");
        var permissions = effective.GetProperty("body").GetProperty("permissions")
            .EnumerateArray().Select(entry => entry.GetString()).ToArray();

        Assert.Equal(200, effective.GetProperty("status").GetInt32());
        Assert.Contains("organisation.destroy", permissions);
        Assert.Contains("licensing.manage", permissions);
    }

    [Fact]
    public async Task Users_me_api_reports_the_resolved_permissions()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);

        var me = await GetJsonAsync(page, "/api/users/me");
        var permissions = me.GetProperty("body").GetProperty("permissions")
            .EnumerateArray().Select(entry => entry.GetString()).ToArray();

        Assert.Contains("organisation.read", permissions);
        Assert.Contains("teams.manage", permissions);
    }

    [Fact]
    public async Task People_member_detail_opens_from_the_members_grid()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.OpenPeopleAsync(page);

        await Ui.ClickAsync(page.Locator("tr.click-row[data-member]").First);
        await Assertions.Expect(page.Locator("#content h1")).Not.ToHaveTextAsync("People");
        await Assertions.Expect(page.Locator("[data-member-tab='permissions']")).ToBeVisibleAsync();
        await Ui.ClickAsync(page.Locator("[data-member-tab='permissions']"));
        await Assertions.Expect(page.Locator("[data-explain]").First).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Ui.ClickAsync(page.Locator("[data-explain]").First);
        await Assertions.Expect(page.Locator("#permissionDrawer:not([hidden])")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Assertions.Expect(page.Locator("#permissionDrawerTitle")).ToContainTextAsync("Why");
    }

    [Fact]
    public async Task People_team_detail_opens_and_shows_settings()
    {
        var suffix = Suffix();
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await CreateTeamAsync(page, $"Detail Team {suffix}", $"detail-team-{suffix}");

        await Ui.ClickAsync(page.Locator("tr.click-row[data-team]").Filter(new() { HasText = $"Detail Team {suffix}" }));
        await Assertions.Expect(page.Locator("#content h1")).ToContainTextAsync($"Detail Team {suffix}");
        await Ui.ClickAsync(page.Locator("[data-team-tab='settings']"));
        await Assertions.Expect(page.Locator("#teamDetailRole")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task New_project_wizard_offers_an_owning_team()
    {
        await using var session = await NewSessionAsync();
        var page = session.Page;
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/projects");
        await Ui.ClickAsync(page.Locator("#projectsNew"));
        await Ui.ExpectModalOpenAsync(page, "Create project");
        await Assertions.Expect(page.Locator("#newProjectTeam")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#newProjectTeam option").CountAsync() >= 1);
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> GetJsonAsync(IPage page, string path)
    {
        var raw = await page.EvaluateAsync<string>("""
            async (path) => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch(path, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
              return JSON.stringify({ status: response.status, body: await response.json() });
            }
            """, path);
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static async Task OpenPermissionsAsync(IPage page)
    {
        await Ui.EnsureOrgNavAsync(page);
        await Ui.GoToHashAsync(page, "/organisation/settings/permissions");
        await Assertions.Expect(page.Locator("#accessRolesList")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private static async Task OpenTeamsAsync(IPage page)
    {
        await Ui.OpenPeopleAsync(page);
        await Ui.ClickAsync(page.Locator("#peopleTeams"));
        await Ui.ExpectVisible(page, "h1", "Teams");
    }

    private static async Task OpenProjectMembersAsync(IPage page)
    {
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/settings/members");
        await Assertions.Expect(page.Locator("#accessMembersTab")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Ui.ClickAsync(page.Locator("#accessMembersTab"));
        await Assertions.Expect(page.Locator("#addProjectMember")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private static async Task OpenProjectTeamsAccessAsync(IPage page)
    {
        await Ui.EnsureProjectNavAsync(page);
        await Ui.GoToHashAsync(page, "/settings/members");
        await Assertions.Expect(page.Locator("#accessTeamsTab")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Ui.ClickAsync(page.Locator("#accessTeamsTab"));
        await Assertions.Expect(page.Locator("#addProjectTeam")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private static async Task OpenTeamSettingsAsync(IPage page, string teamName)
    {
        await OpenTeamsAsync(page);
        await Ui.ClickAsync(page.Locator("tr.module-card, .module-card").Filter(new() { HasText = teamName }).First);
        await Assertions.Expect(page.Locator("[data-team-tab='settings']")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Ui.ClickAsync(page.Locator("[data-team-tab='settings']"));
        await Assertions.Expect(page.Locator("#teamDetailRole")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private static async Task CreateTeamAsync(IPage page, string name, string slug, string? roleName = null)
    {
        await OpenTeamsAsync(page);
        await Ui.ClickAsync(page.Locator("#createTeam"));
        await page.Locator("#teamName").FillAsync(name);
        await page.Locator("#teamSlug").FillAsync(slug);
        if (roleName is not null)
        {
            await page.Locator("#teamRole").SelectOptionAsync(new SelectOptionValue { Label = roleName });
        }

        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));
        await Ui.ExpectToastAsync(page, "Team created");
        await OpenTeamsAsync(page);
    }

    private static async Task CreateRoleAsync(IPage page, string name, string slug, params string[] permissions)
    {
        await Ui.ClickAsync(page.Locator("#createAccessRole"));
        await Ui.ExpectModalOpenAsync(page, "New custom role");
        await page.Locator("#roleName").FillAsync(name);
        await page.Locator("#roleSlug").FillAsync(slug);
        foreach (var permission in permissions)
        {
            await Ui.ClickAsync(page.Locator($"[data-role-perm='{permission}']"));
        }

        await Ui.ClickAsync(page.Locator("#modal[open] button[value='submit']"));
    }
}
