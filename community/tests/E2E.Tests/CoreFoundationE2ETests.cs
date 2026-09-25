using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace E2E.Tests;

/// <summary>
/// End-to-end Core Foundation journeys against a fresh installation (empty database).
/// Exercises setup → project → repository → invite → team access → role boundaries.
/// </summary>
public sealed class CoreFoundationE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public CoreFoundationE2ETests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Fresh_install_onboards_organisation_owner_project_and_repository()
    {
        using var host = CreateEmptyHost();
        using var client = host.CreateClient();

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("app.js", html);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.False(status.GetProperty("initialised").GetBoolean());

        var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Northstar Engineering",
            organisationDescription = "Self-hosted delivery",
            displayName = "Rowan Smith",
            username = "rowan",
            email = "rowan@example.com",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var setupDoc = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        var token = setupDoc.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal("Owner", me.GetProperty("membership").GetProperty("role").GetString());
        Assert.Equal("rowan", me.GetProperty("user").GetProperty("username").GetString());

        var project = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Platform",
            slug = "platform",
            description = "Modular software delivery platform",
            visibility = "Private"
        });
        project.EnsureSuccessStatusCode();
        using var projectDoc = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        var projectId = projectDoc.RootElement.GetProperty("id").GetGuid();

        var bySlug = await client.GetFromJsonAsync<JsonElement>("/api/projects/platform");
        Assert.Equal(projectId, bySlug.GetProperty("id").GetGuid());

        var repository = await client.PostAsJsonAsync($"/api/projects/{projectId}/repositories", new
        {
            name = "platform",
            slug = "platform",
            defaultBranch = "main",
            providerType = "github",
            externalOwner = "example",
            externalName = "platform",
            cloneUrl = "https://github.com/example/platform.git",
            webUrl = "https://github.com/example/platform"
        });
        repository.EnsureSuccessStatusCode();
        using var repoDoc = JsonDocument.Parse(await repository.Content.ReadAsStringAsync());
        var repositoryId = repoDoc.RootElement.GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/projects/{projectId}/repositories/{repositoryId}/workspace", new
        {
            localPath = @"C:\Development\Platform"
        })).EnsureSuccessStatusCode();

        var ready = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.True(ready.GetProperty("initialised").GetBoolean());
        Assert.True(ready.GetProperty("hasProjects").GetBoolean());
        Assert.True(ready.GetProperty("hasRepositories").GetBoolean());

        var secondSetup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Other",
            displayName = "Other",
            username = "other",
            email = "other@example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Conflict, secondSetup.StatusCode);
    }

    [Fact]
    public async Task Staged_setup_organisation_licence_modules_owner_without_project()
    {
        using var host = CreateEmptyHost();
        using var client = host.CreateClient();

        var bootstrap = await client.PostAsJsonAsync("/api/setup/bootstrap-login", new { username = "admin", password = "admin" });
        bootstrap.EnsureSuccessStatusCode();
        using var bootstrapDoc = JsonDocument.Parse(await bootstrap.Content.ReadAsStringAsync());
        var bootstrapToken = bootstrapDoc.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bootstrapToken);

        (await client.PostAsJsonAsync("/api/setup/organisation", new { name = "Lean Labs", description = "Lean onboarding" }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/setup/licence/community", new { })).EnsureSuccessStatusCode();

        var modules = await client.GetFromJsonAsync<JsonElement>("/api/setup/modules");
        Assert.True(modules.GetArrayLength() > 0);

        (await client.PostAsJsonAsync("/api/setup/modules", new
        {
            extensionIds = new[] { "forgedeck.code", "forgedeck.review" },
            skip = false
        })).EnsureSuccessStatusCode();

        var owner = await client.PostAsJsonAsync("/api/setup/owner", new
        {
            displayName = "Lean Owner",
            username = "leanowner",
            email = "lean@example.com",
            password = "password123"
        });
        owner.EnsureSuccessStatusCode();
        using var ownerDoc = JsonDocument.Parse(await owner.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ownerDoc.RootElement.GetProperty("token").GetString()!);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.True(status.GetProperty("initialised").GetBoolean());
        Assert.True(status.GetProperty("hasModules").GetBoolean());
        Assert.True(status.GetProperty("hasOwner").GetBoolean());
        Assert.False(status.GetProperty("hasProjects").GetBoolean());
        Assert.DoesNotContain(status.GetProperty("steps").EnumerateArray(),
            s => s.GetProperty("id").GetString() == "project");

        var project = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Atlas",
            slug = "atlas",
            visibility = "Private",
            enabledModuleIds = new[] { "forgedeck.code" }
        });
        project.EnsureSuccessStatusCode();
        using var projectDoc = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        var projectId = projectDoc.RootElement.GetProperty("id").GetGuid();

        var projectModules = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/modules");
        Assert.Contains(projectModules.EnumerateArray(),
            m => m.GetProperty("extensionId").GetString() == "forgedeck.code" && m.GetProperty("enabled").GetBoolean());

        (await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/install", new { enable = true }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/projects/{projectId}/modules", new
        {
            enabledExtensionIds = new[] { "forgedeck.code", "forgedeck.review" }
        })).EnsureSuccessStatusCode();

        var afterEnable = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/modules");
        Assert.Contains(afterEnable.EnumerateArray(),
            m => m.GetProperty("extensionId").GetString() == "forgedeck.review" && m.GetProperty("enabled").GetBoolean());

        (await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/disable", new { })).EnsureSuccessStatusCode();
        var afterOrgDisable = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/modules");
        var reviewAfter = afterOrgDisable.EnumerateArray()
            .First(m => m.GetProperty("extensionId").GetString() == "forgedeck.review");
        Assert.False(reviewAfter.GetProperty("organisationEnabled").GetBoolean());
    }

    [Fact]
    public async Task Setup_session_rejects_stale_bearer_token()
    {
        using var host = CreateEmptyHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "stale-token");
        var response = await client.GetAsync("/api/setup/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invite_accept_team_grant_and_private_project_isolation()
    {
        using var host = CreateEmptyHost();
        using var owner = await BootstrapAsync(host, "e2e-owner@example.com", "e2eowner", "E2E Owner");

        var project = await owner.PostAsJsonAsync("/api/projects", new
        {
            name = "SecretPlatform",
            slug = "secret-platform",
            visibility = "Private"
        });
        project.EnsureSuccessStatusCode();
        using var projectDoc = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        var projectId = projectDoc.RootElement.GetProperty("id").GetGuid();

        var team = await owner.PostAsJsonAsync("/api/teams", new { name = "Backend", slug = "backend", description = "API" });
        team.EnsureSuccessStatusCode();
        using var teamDoc = JsonDocument.Parse(await team.Content.ReadAsStringAsync());
        var teamId = teamDoc.RootElement.GetProperty("id").GetGuid();

        var invite = await owner.PostAsJsonAsync("/api/organisation/invitations", new { email = "bob@example.com", role = "Member" });
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var inviteToken = inviteDoc.RootElement.GetProperty("token").GetString()!;
        Assert.Contains("/invite/", inviteDoc.RootElement.GetProperty("acceptPath").GetString());

        using var bobAnonymous = host.CreateClient();
        var preview = await bobAnonymous.GetFromJsonAsync<JsonElement>($"/api/invitations/{Uri.EscapeDataString(inviteToken)}");
        Assert.Equal("bob@example.com", preview.GetProperty("email").GetString());

        var accept = await bobAnonymous.PostAsJsonAsync($"/api/invitations/{Uri.EscapeDataString(inviteToken)}/accept", new
        {
            username = "bob",
            displayName = "Bob",
            password = "password123"
        });
        accept.EnsureSuccessStatusCode();
        using var acceptDoc = JsonDocument.Parse(await accept.Content.ReadAsStringAsync());
        using var bob = Authenticated(host, acceptDoc.RootElement.GetProperty("token").GetString()!);
        var bobId = acceptDoc.RootElement.GetProperty("user").GetProperty("id").GetGuid();

        Assert.Equal(0, (await bob.GetFromJsonAsync<JsonElement>("/api/projects")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/projects/secret-platform")).StatusCode);

        (await owner.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = bobId })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/projects/{projectId}/teams", new { teamId })).EnsureSuccessStatusCode();

        var visible = await bob.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(1, visible.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/projects/secret-platform")).StatusCode);

        (await owner.PostAsJsonAsync("/api/core/context/project", new { projectId })).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync("/api/core/context/project", new { projectId })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Multiple_owners_and_last_owner_protection_end_to_end()
    {
        using var host = CreateEmptyHost();
        using var rowan = await BootstrapAsync(host, "rowan-e2e@example.com", "rowane2e", "Rowan");
        var rowanMe = await rowan.GetFromJsonAsync<JsonElement>("/api/users/me");
        var rowanId = rowanMe.GetProperty("user").GetProperty("id").GetGuid();

        var aliceUser = await rowan.PostAsJsonAsync("/api/organisation/members", new
        {
            email = "alice-e2e@example.com",
            username = "alicee2e",
            displayName = "Alice",
            password = "password123",
            role = "Admin"
        });
        aliceUser.EnsureSuccessStatusCode();
        using var aliceDoc = JsonDocument.Parse(await aliceUser.Content.ReadAsStringAsync());
        var aliceId = aliceDoc.RootElement.GetProperty("id").GetGuid();

        using var login = host.CreateClient();
        var aliceLogin = await login.PostAsJsonAsync("/api/auth/login", new { email = "alice-e2e@example.com", password = "password123" });
        aliceLogin.EnsureSuccessStatusCode();
        using var aliceLoginDoc = JsonDocument.Parse(await aliceLogin.Content.ReadAsStringAsync());
        using var alice = Authenticated(host, aliceLoginDoc.RootElement.GetProperty("token").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{aliceId}/role")
        {
            Content = JsonContent.Create(new { role = "Owner" })
        })).StatusCode);

        (await rowan.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{aliceId}/role")
        {
            Content = JsonContent.Create(new { role = "Owner" })
        })).EnsureSuccessStatusCode();

        (await rowan.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{rowanId}/role")
        {
            Content = JsonContent.Create(new { role = "Member" })
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await alice.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{aliceId}/role")
        {
            Content = JsonContent.Create(new { role = "Admin" })
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"/api/organisation/members/{aliceId}")).StatusCode);
    }

    [Fact]
    public async Task Spa_assets_and_module_navigation_load_on_seeded_full_host()
    {
        using var client = CreateSeededClient("Development");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.css")).StatusCode);

        // Seeded host is unauthenticated by default for static assets; API needs the dogfood owner token.
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginDoc.RootElement.GetProperty("token").GetString());

        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.Contains("\"id\":\"review\"", modules);
        Assert.Contains("\"id\":\"pipelines\"", modules);

        var people = await client.GetAsync("/api/organisation/members");
        Assert.Equal(HttpStatusCode.OK, people.StatusCode);
        var org = await client.GetAsync("/api/organisation");
        Assert.Equal(HttpStatusCode.OK, org.StatusCode);
        var projects = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.OK, projects.StatusCode);
    }

    [Fact]
    public async Task Used_invitation_cannot_be_accepted_twice()
    {
        using var host = CreateEmptyHost();
        using var owner = await BootstrapAsync(host, "invite2@example.com", "invite2", "Invite Two");
        var invite = await owner.PostAsJsonAsync("/api/organisation/invitations", new { email = "once@example.com", role = "Member" });
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var token = inviteDoc.RootElement.GetProperty("token").GetString()!;

        using var first = host.CreateClient();
        (await first.PostAsJsonAsync($"/api/invitations/{Uri.EscapeDataString(token)}/accept", new
        {
            username = "once",
            displayName = "Once",
            password = "password123"
        })).EnsureSuccessStatusCode();

        using var second = host.CreateClient();
        var reuse = await second.PostAsJsonAsync($"/api/invitations/{Uri.EscapeDataString(token)}/accept", new
        {
            username = "once2",
            displayName = "Once Two",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
    }

    private async Task<HttpClient> BootstrapAsync(
        WebApplicationFactory<Program> host,
        string email,
        string username,
        string displayName)
    {
        using var setupClient = host.CreateClient();
        var setup = await setupClient.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Northstar Engineering",
            displayName,
            username,
            email,
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        return Authenticated(host, body.RootElement.GetProperty("token").GetString()!);
    }

    private WebApplicationFactory<Program> CreateEmptyHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-core-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-core-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "false");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
        });
    }

    private HttpClient CreateSeededClient(string environment)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-seeded-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-seeded-keys-{Guid.NewGuid():N}");
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }

    private static HttpClient Authenticated(WebApplicationFactory<Program> host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
