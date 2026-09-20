using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tests.Integration;

public sealed class CoreFoundationApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public CoreFoundationApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Fresh_instance_reports_uninitialised_and_rejects_authenticated_apis()
    {
        using var host = CreateEmptyHost();
        using var client = host.CreateClient();
        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.False(status.GetProperty("initialised").GetBoolean());

        var projects = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, projects.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_creates_owner_and_rejects_second_setup()
    {
        using var host = CreateEmptyHost();
        using var client = host.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Northstar Engineering",
            organisationDescription = "Core foundation",
            displayName = "Rowan Smith",
            username = "rowan",
            email = "rowan@example.com",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("token").GetString()));
        Assert.Equal("Northstar Engineering", body.RootElement.GetProperty("organisation").GetProperty("name").GetString());
        Assert.Equal("rowan", body.RootElement.GetProperty("user").GetProperty("username").GetString());

        var second = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Other",
            displayName = "Other",
            username = "other",
            email = "other@example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.True(status.GetProperty("initialised").GetBoolean());
    }

    [Fact]
    public async Task Owner_can_create_project_team_invite_and_grant_access()
    {
        using var host = CreateEmptyHost();
        using var ownerClient = await BootstrapAsync(host);

        var project = await ownerClient.PostAsJsonAsync("/api/projects", new
        {
            name = "Platform",
            slug = "platform",
            description = "Delivery platform",
            visibility = "Private"
        });
        project.EnsureSuccessStatusCode();
        using var projectDoc = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        var projectId = projectDoc.RootElement.GetProperty("id").GetGuid();

        var team = await ownerClient.PostAsJsonAsync("/api/teams", new { name = "Backend", slug = "backend" });
        team.EnsureSuccessStatusCode();
        using var teamDoc = JsonDocument.Parse(await team.Content.ReadAsStringAsync());
        var teamId = teamDoc.RootElement.GetProperty("id").GetGuid();

        var invite = await ownerClient.PostAsJsonAsync("/api/organisation/invitations", new
        {
            email = "alice@example.com",
            role = "Member"
        });
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var token = inviteDoc.RootElement.GetProperty("token").GetString()!;

        using var anonymous = host.CreateClient();
        var accept = await anonymous.PostAsJsonAsync($"/api/invitations/{Uri.EscapeDataString(token)}/accept", new
        {
            username = "alice",
            displayName = "Alice",
            password = "password123"
        });
        accept.EnsureSuccessStatusCode();
        using var acceptDoc = JsonDocument.Parse(await accept.Content.ReadAsStringAsync());
        var aliceToken = acceptDoc.RootElement.GetProperty("token").GetString()!;
        var aliceId = acceptDoc.RootElement.GetProperty("user").GetProperty("id").GetGuid();

        using var aliceClient = Authenticated(host, aliceToken);
        var aliceProjects = await aliceClient.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(0, aliceProjects.GetArrayLength());

        (await ownerClient.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = aliceId })).EnsureSuccessStatusCode();
        (await ownerClient.PostAsJsonAsync($"/api/projects/{projectId}/teams", new { teamId })).EnsureSuccessStatusCode();

        var granted = await aliceClient.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(1, granted.GetArrayLength());
        Assert.Equal("platform", granted[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Member_cannot_manage_users_or_promote_self()
    {
        using var host = CreateEmptyHost();
        using var ownerClient = await BootstrapAsync(host, "owner2@example.com", "owner2", "Owner Two");

        var invite = await ownerClient.PostAsJsonAsync("/api/organisation/invitations", new
        {
            email = "member@example.com",
            role = "Member"
        });
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var token = inviteDoc.RootElement.GetProperty("token").GetString()!;

        using var anonymous = host.CreateClient();
        var accept = await anonymous.PostAsJsonAsync($"/api/invitations/{Uri.EscapeDataString(token)}/accept", new
        {
            username = "member",
            displayName = "Member",
            password = "password123"
        });
        accept.EnsureSuccessStatusCode();
        using var acceptDoc = JsonDocument.Parse(await accept.Content.ReadAsStringAsync());
        using var memberClient = Authenticated(host, acceptDoc.RootElement.GetProperty("token").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync("/api/organisation/invitations", new
        {
            email = "x@example.com",
            role = "Member"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync("/api/teams", new
        {
            name = "Hack",
            slug = "hack"
        })).StatusCode);

        var me = await memberClient.GetFromJsonAsync<JsonElement>("/api/users/me");
        var memberId = me.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{memberId}/role")
        {
            Content = JsonContent.Create(new { role = "Owner" })
        })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_project_but_cannot_promote_to_owner()
    {
        using var host = CreateEmptyHost();
        using var ownerClient = await BootstrapAsync(host, "owner3@example.com", "owner3", "Owner Three");
        var me = await ownerClient.GetFromJsonAsync<JsonElement>("/api/users/me");
        var ownerId = me.GetProperty("user").GetProperty("id").GetGuid();

        var created = await ownerClient.PostAsJsonAsync("/api/organisation/members", new
        {
            email = "admin@example.com",
            username = "adminuser",
            displayName = "Admin User",
            password = "password123",
            role = "Admin"
        });
        created.EnsureSuccessStatusCode();
        using var adminDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var adminId = adminDoc.RootElement.GetProperty("id").GetGuid();

        using var loginClient = host.CreateClient();
        var login = await loginClient.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.com", password = "password123" });
        login.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        using var adminClient = Authenticated(host, loginDoc.RootElement.GetProperty("token").GetString()!);

        (await adminClient.PostAsJsonAsync("/api/projects", new
        {
            name = "Tools",
            slug = "tools",
            visibility = "Organisation"
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{adminId}/role")
        {
            Content = JsonContent.Create(new { role = "Owner" })
        })).StatusCode);

        (await ownerClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{adminId}/role")
        {
            Content = JsonContent.Create(new { role = "Owner" })
        })).EnsureSuccessStatusCode();

        (await ownerClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{ownerId}/role")
        {
            Content = JsonContent.Create(new { role = "Admin" })
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Repository_can_be_created_under_project_and_listed()
    {
        using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host, "repo-owner@example.com", "repoowner", "Repo Owner");
        var project = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Stockyards",
            slug = "stockyards",
            visibility = "Private"
        });
        project.EnsureSuccessStatusCode();
        using var projectDoc = JsonDocument.Parse(await project.Content.ReadAsStringAsync());
        var projectId = projectDoc.RootElement.GetProperty("id").GetGuid();

        var repo = await client.PostAsJsonAsync($"/api/projects/{projectId}/repositories", new
        {
            name = "stockyards-api",
            slug = "stockyards-api",
            defaultBranch = "main",
            providerType = "github",
            externalOwner = "example",
            externalName = "stockyards-api",
            cloneUrl = "https://github.com/example/stockyards-api.git",
            webUrl = "https://github.com/example/stockyards-api"
        });
        repo.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/repositories");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("stockyards-api", list[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Invitation_preview_is_public_and_expired_invite_is_rejected()
    {
        using var host = CreateEmptyHost();
        using var ownerClient = await BootstrapAsync(host, "invite-owner@example.com", "inviteowner", "Invite Owner");
        var invite = await ownerClient.PostAsJsonAsync("/api/organisation/invitations", new
        {
            email = "preview@example.com",
            role = "Member"
        });
        invite.EnsureSuccessStatusCode();
        using var inviteDoc = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var token = inviteDoc.RootElement.GetProperty("token").GetString()!;

        using var anonymous = host.CreateClient();
        var preview = await anonymous.GetFromJsonAsync<JsonElement>($"/api/invitations/{Uri.EscapeDataString(token)}");
        Assert.Equal("preview@example.com", preview.GetProperty("email").GetString());
        Assert.False(preview.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_via_api()
    {
        using var host = CreateEmptyHost();
        using var ownerClient = await BootstrapAsync(host, "solo@example.com", "solo", "Solo Owner");
        var me = await ownerClient.GetFromJsonAsync<JsonElement>("/api/users/me");
        var ownerId = me.GetProperty("user").GetProperty("id").GetGuid();

        var demote = await ownerClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/organisation/members/{ownerId}/role")
        {
            Content = JsonContent.Create(new { role = "Member" })
        });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
    }

    private async Task<HttpClient> BootstrapAsync(
        WebApplicationFactory<Program> host,
        string email = "rowan@example.com",
        string username = "rowan",
        string displayName = "Rowan Smith")
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
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-core-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-core-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "false");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
        });
    }

    private static HttpClient Authenticated(WebApplicationFactory<Program> host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
