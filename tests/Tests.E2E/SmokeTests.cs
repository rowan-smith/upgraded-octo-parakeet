using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tests.E2E;

/// <summary>
/// Lightweight host smoke: modules load and core review/pipelines routes respond.
/// </summary>
public sealed class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public SmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Full_host_serves_modules_review_and_pipelines()
    {
        using var client = CreateClient("Full");
        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.Contains("\"id\":\"review\"", modules);
        Assert.Contains("\"id\":\"pipelines\"", modules);
        Assert.Contains("\"id\":\"checks\"", modules);

        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/review/changes")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/pipelines/definitions")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/pipelines/runners")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    private HttpClient CreateClient(string environment)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            var database = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-{Guid.NewGuid():N}.db");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Platform"] = $"Data Source={database}",
                ["Data:ProtectionKeysPath"] = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-keys-{Guid.NewGuid():N}"),
                ["Pipelines:ExecutionMode"] = "Simulated"
            }));
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}
