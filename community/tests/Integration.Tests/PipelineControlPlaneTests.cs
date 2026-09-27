using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class PipelineControlPlaneTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PipelineControlPlaneTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Manual_run_cancel_definition_toggle_and_runner_token_work()
    {
        using var client = AuthenticatedClient(CreateHost("Full"));

        var definitions = await client.GetStringAsync("/api/pipelines/definitions");
        Assert.Contains(".NET Validation", definitions);
        using var defDoc = JsonDocument.Parse(definitions);
        var definitionId = defDoc.RootElement.EnumerateArray().First().GetProperty("id").GetGuid();

        var runResponse = await client.PostAsJsonAsync("/api/pipelines/runs", new
        {
            definitionId,
            @ref = "main",
            commitSha = "abc1234deadbeef",
            repositoryUrl = "https://github.com/rowan-smith/upgraded-octo-parakeet.git"
        });
        runResponse.EnsureSuccessStatusCode();
        using var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
        var runId = runDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("https://github.com/rowan-smith/upgraded-octo-parakeet.git",
            runDoc.RootElement.GetProperty("repositoryUrl").GetString());

        var cancel = await client.PostAsJsonAsync($"/api/pipelines/runs/{runId}/cancel", new { reason = "test cancel" });
        cancel.EnsureSuccessStatusCode();
        Assert.Contains("Cancelled", await cancel.Content.ReadAsStringAsync());

        var disable = await client.PostAsync($"/api/pipelines/definitions/{definitionId}/disable", null);
        disable.EnsureSuccessStatusCode();
        var enable = await client.PostAsync($"/api/pipelines/definitions/{definitionId}/enable", null);
        enable.EnsureSuccessStatusCode();

        var token = await client.PostAsJsonAsync("/api/pipelines/runners/registration-tokens", new { lifetimeHours = 1 });
        token.EnsureSuccessStatusCode();
        Assert.Contains("token", await token.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> CreateHost(string environment)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-pipe-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        var cs = $"Data Source={database}";
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", cs);
            builder.UseSetting("ConnectionStrings:Build", cs);
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Runner");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = client.PostAsJsonAsync("/api/auth/login", new { email = "maya@forgedeck.dev", password = "demo" }).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var token = doc.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
