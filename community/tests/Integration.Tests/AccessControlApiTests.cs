using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class AccessControlApiTests
{
    private const string OwnerEmail = "owner@access.dev";
    private const string MemberEmail = "member@access.dev";
    private const string Password = "password123";

    public static IEnumerable<object[]> ProtectedPaths() =>
        new[] { "/api/access/permissions", "/api/access/roles", "/api/access/effective" }
            .Select(path => new object[] { path });

    public static IEnumerable<object[]> SystemRoleSlugs() =>
        new[] { "owner", "admin", "member", "reader", "developer", "reviewer", "builder", "deployer" }
            .Select(slug => new object[] { slug });

    public static IEnumerable<object[]> CataloguePermissions() =>
        new[]
        {
            "organisation.read", "organisation.manage", "organisation.destroy", "users.read", "users.manage",
            "teams.manage", "projects.create", "projects.manage", "audit.read", "licensing.manage",
            "source.repository.read", "review.read", "review.merge", "git.repository.push",
            "pipelines.run", "pipelines.runner.manage", "deploy.read", "deploy.execute", "deploy.manage"
        }.Select(permission => new object[] { permission });

    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Access_endpoints_require_authentication(string path)
    {
        await using var host = CreateHost();
        using var anonymous = host.CreateClient();

        var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Catalogue_lists_every_permission_with_metadata()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var catalogue = await owner.GetFromJsonAsync<JsonElement>("/api/access/permissions");

        Assert.Equal(32, catalogue.GetArrayLength());
        Assert.All(catalogue.EnumerateArray(), entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("key").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
        });
    }

    [Theory]
    [MemberData(nameof(CataloguePermissions))]
    public async Task Catalogue_contains_permission(string permission)
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var catalogue = await owner.GetFromJsonAsync<JsonElement>("/api/access/permissions");

        Assert.Contains(catalogue.EnumerateArray(), entry => entry.GetProperty("key").GetString() == permission);
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public async Task Built_in_role_is_listed_as_a_system_role(string slug)
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/access/roles");
        var role = roles.EnumerateArray().Single(entry => entry.GetProperty("slug").GetString() == slug);

        Assert.True(role.GetProperty("isSystem").GetBoolean());
        Assert.True(role.GetProperty("permissions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Role_list_starts_with_the_eight_built_in_roles()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/access/roles");

        Assert.Equal(8, roles.EnumerateArray().Count(entry => entry.GetProperty("isSystem").GetBoolean()));
    }

    [Fact]
    public async Task Owner_can_create_read_update_and_delete_a_custom_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var created = await owner.PostAsJsonAsync("/api/access/roles", new
        {
            name = "Release Captain",
            description = "Ships releases",
            permissions = new[] { "deploy.read", "deploy.execute" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = role.GetProperty("id").GetGuid();
        Assert.Equal("release-captain", role.GetProperty("slug").GetString());
        Assert.False(role.GetProperty("isSystem").GetBoolean());

        var fetched = await owner.GetFromJsonAsync<JsonElement>($"/api/access/roles/{id}");
        Assert.Equal(2, fetched.GetProperty("permissions").GetArrayLength());

        var patched = await owner.PatchAsJsonAsync($"/api/access/roles/{id}", new
        {
            name = "Release Manager",
            permissions = new[] { "deploy.read" }
        });
        patched.EnsureSuccessStatusCode();
        var updated = await patched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Release Manager", updated.GetProperty("name").GetString());
        Assert.Equal(1, updated.GetProperty("permissions").GetArrayLength());

        var deleted = await owner.DeleteAsync($"/api/access/roles/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/access/roles/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("organisation.delete")]
    [InlineData("review.publish")]
    [InlineData("*")]
    public async Task Unknown_permission_is_rejected_when_creating_a_role(string permission)
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var response = await owner.PostAsJsonAsync("/api/access/roles", new
        {
            name = "Bad",
            permissions = new[] { permission }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public async Task Built_in_role_slugs_cannot_be_reused(string slug)
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var response = await owner.PostAsJsonAsync("/api/access/roles", new
        {
            name = "Clash",
            slug,
            permissions = new[] { "review.read" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Built_in_roles_cannot_be_edited_or_deleted()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var reader = await FindRoleAsync(owner, "reader");

        var patched = await owner.PatchAsJsonAsync($"/api/access/roles/{reader}", new { name = "Renamed" });
        var deleted = await owner.DeleteAsync($"/api/access/roles/{reader}");

        Assert.Equal(HttpStatusCode.BadRequest, patched.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
    }

    [Fact]
    public async Task Unknown_role_ids_report_not_found()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var missing = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/access/roles/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/access/roles/{missing}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PatchAsJsonAsync($"/api/access/roles/{missing}", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Member_cannot_manage_roles_but_can_read_them()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");

        var list = await member.GetAsync("/api/access/roles");
        var create = await member.PostAsJsonAsync("/api/access/roles", new { name = "Sneaky", permissions = new[] { "review.read" } });
        var reader = await FindRoleAsync(owner, "reader");
        var delete = await member.DeleteAsync($"/api/access/roles/{reader}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Admin_can_manage_roles()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var admin = await AddMemberAndSignInAsync(host, owner, "Admin", "admin@access.dev", "adminuser", "Admin");

        var create = await admin.PostAsJsonAsync("/api/access/roles", new { name = "Auditor", permissions = new[] { "audit.read" } });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SystemRoleSlugs))]
    public async Task Team_role_can_be_assigned_through_the_api(string slug)
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var team = await CreateTeamAsync(owner, "Backend", "backend");
        var role = await FindRoleAsync(owner, slug);

        var assigned = await owner.PatchAsJsonAsync($"/api/teams/{team}/role", new { roleId = role });
        assigned.EnsureSuccessStatusCode();

        var teams = await owner.GetFromJsonAsync<JsonElement>("/api/teams");
        var stored = teams.EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == team);
        Assert.Equal(role, stored.GetProperty("roleId").GetGuid());
    }

    [Fact]
    public async Task Team_role_can_be_cleared_through_the_api()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var team = await CreateTeamAsync(owner, "Backend", "backend");
        var reader = await FindRoleAsync(owner, "reader");
        (await owner.PatchAsJsonAsync($"/api/teams/{team}/role", new { roleId = reader })).EnsureSuccessStatusCode();

        (await owner.PatchAsJsonAsync($"/api/teams/{team}/role", new { roleId = (Guid?)null })).EnsureSuccessStatusCode();

        var teams = await owner.GetFromJsonAsync<JsonElement>("/api/teams");
        var stored = teams.EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == team);
        Assert.Equal(JsonValueKind.Null, stored.GetProperty("roleId").ValueKind);
    }

    [Fact]
    public async Task Team_can_be_created_with_a_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var reader = await FindRoleAsync(owner, "reader");

        var created = await owner.PostAsJsonAsync("/api/teams", new { name = "Readers", slug = "readers", roleId = reader });
        created.EnsureSuccessStatusCode();
        var team = await created.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(reader, team.GetProperty("roleId").GetGuid());
    }

    [Fact]
    public async Task Assigning_an_unknown_role_or_team_reports_not_found()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var team = await CreateTeamAsync(owner, "Backend", "backend");
        var reader = await FindRoleAsync(owner, "reader");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PatchAsJsonAsync($"/api/teams/{team}/role", new { roleId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PatchAsJsonAsync($"/api/teams/{Guid.NewGuid()}/role", new { roleId = reader })).StatusCode);
    }

    [Fact]
    public async Task Member_cannot_assign_a_team_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");
        var team = await CreateTeamAsync(owner, "Backend", "backend");
        var reader = await FindRoleAsync(owner, "reader");

        var response = await member.PatchAsJsonAsync($"/api/teams/{team}/role", new { roleId = reader });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Project_member_grant_accepts_and_reports_a_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        var memberId = await FindMemberIdAsync(owner, MemberEmail);
        var reader = await FindRoleAsync(owner, "reader");

        var granted = await owner.PostAsJsonAsync($"/api/projects/{project}/members", new { userId = memberId, roleId = reader });
        granted.EnsureSuccessStatusCode();

        var members = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{project}/members");
        var grant = members.EnumerateArray().Single(entry => entry.GetProperty("userId").GetGuid() == memberId);
        Assert.Equal(reader, grant.GetProperty("roleId").GetGuid());
        Assert.Equal("Reader", grant.GetProperty("roleName").GetString());
    }

    [Fact]
    public async Task Project_team_grant_accepts_and_reports_a_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        var team = await CreateTeamAsync(owner, "Backend", "backend");
        var builder = await FindRoleAsync(owner, "builder");

        var granted = await owner.PostAsJsonAsync($"/api/projects/{project}/teams", new { teamId = team, roleId = builder });
        granted.EnsureSuccessStatusCode();

        var teams = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{project}/teams");
        var grant = teams.EnumerateArray().Single(entry => entry.GetProperty("teamId").GetGuid() == team);
        Assert.Equal(builder, grant.GetProperty("roleId").GetGuid());
        Assert.Equal("Builder", grant.GetProperty("roleName").GetString());
    }

    [Fact]
    public async Task Granting_a_project_with_an_unknown_role_reports_not_found()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        var memberId = await FindMemberIdAsync(owner, MemberEmail);

        var response = await owner.PostAsJsonAsync(
            $"/api/projects/{project}/members",
            new { userId = memberId, roleId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Effective_permissions_are_narrowed_by_a_custom_role_on_a_team_grant()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");
        var memberId = await FindMemberIdAsync(owner, MemberEmail);
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        var team = await CreateTeamAsync(owner, "Readers", "readers");
        (await owner.PostAsJsonAsync($"/api/teams/{team}/members", new { userId = memberId })).EnsureSuccessStatusCode();

        var created = await owner.PostAsJsonAsync("/api/access/roles", new
        {
            name = "Review Only",
            permissions = new[] { "organisation.read", "review.read" }
        });
        created.EnsureSuccessStatusCode();
        var roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"/api/projects/{project}/teams", new { teamId = team, roleId })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/core/context/project", new { projectId = project })).EnsureSuccessStatusCode();

        var effective = await member.GetFromJsonAsync<JsonElement>("/api/access/effective");
        var permissions = effective.GetProperty("permissions").EnumerateArray().Select(entry => entry.GetString()).ToArray();

        Assert.Equal(new[] { "organisation.read", "review.read" }, permissions);
        Assert.Equal(project, effective.GetProperty("projectId").GetGuid());
    }

    [Fact]
    public async Task Effective_permissions_fall_back_to_the_organisation_ceiling_without_a_role()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member");
        var memberId = await FindMemberIdAsync(owner, MemberEmail);
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        (await owner.PostAsJsonAsync($"/api/projects/{project}/members", new { userId = memberId })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/core/context/project", new { projectId = project })).EnsureSuccessStatusCode();

        var effective = await member.GetFromJsonAsync<JsonElement>("/api/access/effective");
        var permissions = effective.GetProperty("permissions").EnumerateArray().Select(entry => entry.GetString()).ToArray();

        Assert.Contains("review.merge", permissions);
        Assert.DoesNotContain("users.manage", permissions);
    }

    [Fact]
    public async Task A_narrowed_member_loses_the_endpoints_the_role_excludes()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);
        using var member = await AddMemberAndSignInAsync(host, owner, "Member", role: "Admin");
        var memberId = await FindMemberIdAsync(owner, MemberEmail);
        var project = await CreateProjectAsync(owner, "Atlas", "atlas");
        var reader = await FindRoleAsync(owner, "reader");
        (await owner.PostAsJsonAsync($"/api/projects/{project}/members", new { userId = memberId, roleId = reader })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/core/context/project", new { projectId = project })).EnsureSuccessStatusCode();

        // Admin ceiling intersected with Reader leaves no users.manage, so member administration is refused.
        var invite = await member.PostAsJsonAsync("/api/organisation/members", new
        {
            email = "extra@access.dev",
            username = "extra",
            displayName = "Extra",
            password = Password,
            role = "Member"
        });

        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/organisation/members")).StatusCode);
    }

    [Fact]
    public async Task Users_me_reports_the_effective_permissions()
    {
        await using var host = CreateHost();
        using var owner = await SignInOwnerAsync(host);

        var me = await owner.GetFromJsonAsync<JsonElement>("/api/users/me");
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(entry => entry.GetString()).ToArray();

        Assert.Contains("organisation.destroy", permissions);
        Assert.Contains("licensing.manage", permissions);
    }

    private static WebApplicationFactory<Program> CreateHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-access-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-access-keys-{Guid.NewGuid():N}");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "false");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Bootstrap:Username", "admin");
            builder.UseSetting("Bootstrap:Password", "admin");
        });
    }

    private static async Task<HttpClient> SignInOwnerAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Access Labs",
            organisationDescription = "access",
            displayName = "Owner",
            username = "owner",
            email = OwnerEmail,
            password = Password
        });
        setup.EnsureSuccessStatusCode();
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    private static async Task<HttpClient> AddMemberAndSignInAsync(
        WebApplicationFactory<Program> factory,
        HttpClient owner,
        string displayName,
        string email = MemberEmail,
        string username = "memberuser",
        string role = "Member")
    {
        var created = await owner.PostAsJsonAsync("/api/organisation/members", new
        {
            email,
            username,
            displayName,
            password = Password,
            role
        });
        created.EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    private static async Task<Guid> FindRoleAsync(HttpClient client, string slug)
    {
        var roles = await client.GetFromJsonAsync<JsonElement>("/api/access/roles");
        return roles.EnumerateArray().Single(entry => entry.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FindMemberIdAsync(HttpClient client, string email)
    {
        var members = await client.GetFromJsonAsync<JsonElement>("/api/organisation/members");
        return members.EnumerateArray()
            .Single(entry => entry.GetProperty("user").GetProperty("email").GetString() == email)
            .GetProperty("user").GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTeamAsync(HttpClient client, string name, string slug)
    {
        var created = await client.PostAsJsonAsync("/api/teams", new { name, slug });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client, string name, string slug)
    {
        var created = await client.PostAsJsonAsync("/api/projects", new
        {
            name,
            slug,
            visibility = "Private",
            repositoryMode = "SingleRepository"
        });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
