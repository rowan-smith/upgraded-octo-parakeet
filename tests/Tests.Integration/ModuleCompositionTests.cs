using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tests.Integration;

public sealed class ModuleCompositionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ModuleCompositionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Enabled_review_module_registers_navigation_and_routes()
    {
        using var client = AuthenticatedClient(_factory);
        var modules = await client.GetStringAsync("/api/platform/modules");
        var changes = await client.GetAsync("/api/review/changes");

        Assert.Contains("\"id\":\"review\"", modules);
        Assert.Contains("/changes", modules);
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
    }

    [Fact]
    public async Task Disabled_review_module_removes_navigation_and_routes()
    {
        await using var disabled = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Disabled"));
        using var client = AuthenticatedClient(disabled);

        var modules = await client.GetStringAsync("/api/platform/modules");
        var changes = await client.GetAsync("/api/review/changes");

        Assert.DoesNotContain("\"id\":\"review\"", modules);
        Assert.Equal(HttpStatusCode.NotFound, changes.StatusCode);
    }

    [Fact]
    public async Task Imported_change_triggers_pipeline_and_exposes_check_through_contract()
    {
        using var client = AuthenticatedClient(_factory);
        var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
        {
            repositoryUrl = "https://github.com/northstar/atlas",
            externalId = "901"
        });
        imported.EnsureSuccessStatusCode();
        using var change = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
        var changeId = change.RootElement.GetProperty("id").GetGuid();

        var checks = await client.GetStringAsync($"/api/review/changes/{changeId}/checks");

        Assert.Contains("Build and test", checks);
        Assert.Contains("Passed", checks);
    }

    [Fact]
    public async Task Review_operates_without_pipelines_and_has_no_checks_extension()
    {
        await using var reviewOnly = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("ReviewOnly"));
        using var client = AuthenticatedClient(reviewOnly);

        var modules = await client.GetStringAsync("/api/platform/modules");
        var changes = await client.GetAsync("/api/review/changes");
        var pipelineRuns = await client.GetAsync("/api/pipelines/runs");

        Assert.Contains("\"id\":\"review\"", modules);
        Assert.DoesNotContain("\"id\":\"pipelines\"", modules);
        Assert.DoesNotContain("\"id\":\"checks\"", modules);
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pipelineRuns.StatusCode);
    }

    [Fact]
    public async Task Native_git_implements_source_provider_and_push_triggers_pipeline()
    {
        using var client = AuthenticatedClient(_factory);
        var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
        {
            providerId = "native-git",
            repositoryUrl = "native://northstar/atlas-native",
            externalId = "77"
        });
        imported.EnsureSuccessStatusCode();
        Assert.Contains("Native change #77", await imported.Content.ReadAsStringAsync());

        using var repositories = JsonDocument.Parse(await client.GetStringAsync("/api/git/repositories"));
        var repositoryId = repositories.RootElement[0].GetProperty("id").GetGuid();
        var push = await client.PostAsJsonAsync($"/api/git/repositories/{repositoryId}/push", new { branch = "main", message = "Integration push" });
        push.EnsureSuccessStatusCode();

        var runs = await client.GetStringAsync("/api/pipelines/runs");
        Assert.Contains("Push", runs);
        Assert.Contains("Integration push", await push.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Native_git_operates_without_review_or_pipelines()
    {
        await using var gitOnly = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("GitOnly"));
        using var client = AuthenticatedClient(gitOnly);

        var modules = await client.GetStringAsync("/api/platform/modules");
        var repositories = await client.GetAsync("/api/git/repositories");
        var changes = await client.GetAsync("/api/review/changes");
        var runs = await client.GetAsync("/api/pipelines/runs");

        Assert.Contains("\"id\":\"git\"", modules);
        Assert.Equal(HttpStatusCode.OK, repositories.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, changes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, runs.StatusCode);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}
