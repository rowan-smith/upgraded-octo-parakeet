using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class DeployApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DeployApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Environments_list_starts_empty_then_create_and_list()
    {
        using var client = AuthenticatedClient(CreateHost());
        var empty = await client.GetFromJsonAsync<JsonElement[]>("/api/deploy/environments");
        Assert.NotNull(empty);
        Assert.Empty(empty!);

        var create = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production", description = "Prod" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal("production", created.RootElement.GetProperty("name").GetString());

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/deploy/environments");
        Assert.Single(list!);
    }

    [Fact]
    public async Task Community_second_environment_returns_licence_required()
    {
        using var client = AuthenticatedClient(CreateHost());
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production" })).StatusCode);

        var second = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "staging" });
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("Deploy.MultiEnvironment", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deployment_create_list_get_and_rollback()
    {
        using var client = AuthenticatedClient(CreateHost());
        var envResponse = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production" });
        envResponse.EnsureSuccessStatusCode();
        using var envDoc = JsonDocument.Parse(await envResponse.Content.ReadAsStringAsync());
        var envId = envDoc.RootElement.GetProperty("id").GetGuid();

        var deploy = await client.PostAsJsonAsync("/api/deploy/deployments", new
        {
            environmentId = envId,
            version = "1.0.0",
            notes = "first"
        });
        Assert.Equal(HttpStatusCode.Created, deploy.StatusCode);
        using var depDoc = JsonDocument.Parse(await deploy.Content.ReadAsStringAsync());
        var depId = depDoc.RootElement.GetProperty("id").GetGuid();

        var get = await client.GetAsync($"/api/deploy/deployments/{depId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement[]>($"/api/deploy/deployments?environmentId={envId}");
        Assert.Contains(list!, d => d.GetProperty("id").GetGuid() == depId);

        var rollback = await client.PostAsJsonAsync($"/api/deploy/deployments/{depId}/rollback", new { });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        using var rb = JsonDocument.Parse(await rollback.Content.ReadAsStringAsync());
        Assert.Equal(depId, rb.RootElement.GetProperty("rollbackOfId").GetGuid());
    }

    [Fact]
    public async Task Delete_environment_returns_no_content()
    {
        using var client = AuthenticatedClient(CreateHost());
        var create = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "temp" });
        create.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var delete = await client.DeleteAsync($"/api/deploy/environments/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/deploy/environments"))!);
    }

    [Theory]
    [InlineData("/api/deploy/environments")]
    [InlineData("/api/deploy/deployments")]
    public async Task Deploy_list_endpoints_return_ok(string path)
    {
        using var client = AuthenticatedClient(CreateHost());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Missing_environment_and_deployment_return_not_found()
    {
        using var client = AuthenticatedClient(CreateHost());
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/deploy/environments/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/deploy/deployments/{missing}")).StatusCode);
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("1.0.0")]
    [InlineData("2026.09.25")]
    [InlineData("release-candidate")]
    [InlineData("hotfix-42")]
    public async Task Version_labels_deploy_successfully(string version)
    {
        using var client = AuthenticatedClient(CreateHost());
        var envResponse = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production" });
        envResponse.EnsureSuccessStatusCode();
        using var envDoc = JsonDocument.Parse(await envResponse.Content.ReadAsStringAsync());
        var envId = envDoc.RootElement.GetProperty("id").GetGuid();

        var deploy = await client.PostAsJsonAsync("/api/deploy/deployments", new { environmentId = envId, version });
        Assert.Equal(HttpStatusCode.Created, deploy.StatusCode);
        using var dep = JsonDocument.Parse(await deploy.Content.ReadAsStringAsync());
        Assert.Equal(version, dep.RootElement.GetProperty("version").GetString());
    }

    private WebApplicationFactory<Program> CreateHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-deploy-api-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Modules:deploy:Enabled", "true");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}

public sealed class ApiRouteMatrixTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiRouteMatrixTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static IEnumerable<object[]> GetRoutes()
    {
        string[] routes =
        [
            "/api/platform/modules",
            "/api/core/context",
            "/api/projects",
            "/api/users/me",
            "/api/review/changes",
            "/api/pipelines/definitions",
            "/api/pipelines/runs",
            "/api/pipelines/runners",
            "/api/deploy/environments",
            "/api/deploy/deployments",
            "/api/organisation/members",
            "/api/setup/status",
            "/"
        ];
        foreach (var route in routes)
        {
            yield return [route];
        }
    }

    [Theory]
    [MemberData(nameof(GetRoutes))]
    public async Task Seeded_full_host_get_routes_succeed(string route)
    {
        using var client = AuthenticatedClient(CreateHost("Full"));
        var response = await client.GetAsync(route);
        Assert.True(
            response.IsSuccessStatusCode,
            $"{route} returned {(int)response.StatusCode} {response.StatusCode}");
    }

    [Theory]
    [InlineData("Full", "review", true)]
    [InlineData("Full", "pipelines", true)]
    [InlineData("Full", "deploy", true)]
    [InlineData("ReviewOnly", "review", true)]
    [InlineData("ReviewOnly", "pipelines", false)]
    [InlineData("Disabled", "review", false)]
    public async Task Environment_enables_expected_modules(string environment, string moduleId, bool expectedPresent)
    {
        using var client = AuthenticatedClient(CreateHost(environment));
        var modules = await client.GetStringAsync("/api/platform/modules");
        var present = modules.Contains($"\"id\":\"{moduleId}\"", StringComparison.Ordinal);
        Assert.Equal(expectedPresent, present);
    }

    private WebApplicationFactory<Program> CreateHost(string environment)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-routes-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            switch (environment)
            {
                case "Disabled":
                    builder.UseSetting("Modules:review:Enabled", "false");
                    builder.UseSetting("Modules:pipelines:Enabled", "false");
                    builder.UseSetting("Modules:deploy:Enabled", "false");
                    break;
                case "ReviewOnly":
                    builder.UseSetting("Modules:review:Enabled", "true");
                    builder.UseSetting("Modules:pipelines:Enabled", "false");
                    builder.UseSetting("Modules:deploy:Enabled", "false");
                    break;
                default:
                    builder.UseSetting("Modules:deploy:Enabled", "true");
                    break;
            }
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}
