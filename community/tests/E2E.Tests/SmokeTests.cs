using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace E2E.Tests;

public sealed class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Full_host_serves_modules_review_pipelines_and_deploy()
    {
        using var client = CreateClient("Full");
        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.Contains("\"id\":\"review\"", modules);
        Assert.Contains("\"id\":\"pipelines\"", modules);
        Assert.Contains("\"id\":\"deploy\"", modules);
        Assert.Contains("\"id\":\"checks\"", modules);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/review/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pipelines/definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pipelines/runners")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deploy/environments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deploy/deployments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    public static IEnumerable<object[]> SmokeGetRoutes()
    {
        string[] routes =
        [
            "/",
            "/api/platform/modules",
            "/api/core/context",
            "/api/projects",
            "/api/users/me",
            "/api/users/me/starred-projects",
            "/api/organisation/members",
            "/api/review/changes",
            "/api/pipelines/definitions",
            "/api/pipelines/runs",
            "/api/pipelines/runners",
            "/api/deploy/environments",
            "/api/deploy/deployments",
            "/api/setup/status",
            "/api/source/repositories"
        ];
        foreach (var route in routes)
        {
            yield return [route];
        }
    }

    [Theory]
    [MemberData(nameof(SmokeGetRoutes))]
    public async Task Full_host_get_route_succeeds(string route)
    {
        using var client = CreateClient("Full");
        var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, $"{route} => {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Deploy_environment_and_deployment_roundtrip()
    {
        using var client = CreateClient("Full");
        var createEnv = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production", description = "e2e" });
        Assert.Equal(HttpStatusCode.Created, createEnv.StatusCode);
        using var env = JsonDocument.Parse(await createEnv.Content.ReadAsStringAsync());
        var envId = env.RootElement.GetProperty("id").GetGuid();

        var createDep = await client.PostAsJsonAsync("/api/deploy/deployments", new
        {
            environmentId = envId,
            version = "e2e-1.0.0"
        });
        Assert.Equal(HttpStatusCode.Created, createDep.StatusCode);
        using var dep = JsonDocument.Parse(await createDep.Content.ReadAsStringAsync());
        var depId = dep.RootElement.GetProperty("id").GetGuid();

        var rollback = await client.PostAsJsonAsync($"/api/deploy/deployments/{depId}/rollback", new { });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
    }

    private HttpClient CreateClient(string environment)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-keys-{Guid.NewGuid():N}");
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment == "Full" ? "Development" : environment);
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Modules:deploy:Enabled", "true");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}
