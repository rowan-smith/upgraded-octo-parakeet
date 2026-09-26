using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class SearchApiTests
{
    [Fact]
    public async Task Search_requires_authentication()
    {
        await using var host = CreateEmptyHost();
        using var client = host.CreateClient();
        var response = await client.GetAsync("/api/search?q=platform");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_query_returns_shortcuts()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        var payload = await client.GetFromJsonAsync<JsonElement>("/api/search");
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("hits").ValueKind);
        Assert.Contains(payload.GetProperty("hits").EnumerateArray(), h => h.GetProperty("type").GetString() == "route");
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("Platform")]
    [InlineData("plat")]
    public async Task Search_returns_project_hits(string query)
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        await client.PostAsJsonAsync("/api/projects", new { name = "Platform", slug = "platform", description = "Delivery" });

        var payload = await client.GetFromJsonAsync<JsonElement>($"/api/search?q={Uri.EscapeDataString(query)}");
        Assert.Contains(payload.GetProperty("hits").EnumerateArray(), h =>
            h.GetProperty("type").GetString() == "project"
            && h.GetProperty("label").GetString() == "Platform"
            && h.GetProperty("route").GetString() == "/overview");
    }

    [Theory]
    [InlineData("maya")]
    [InlineData("Maya")]
    public async Task Search_returns_person_hits(string query)
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        var payload = await client.GetFromJsonAsync<JsonElement>($"/api/search?q={Uri.EscapeDataString(query)}");
        Assert.Contains(payload.GetProperty("hits").EnumerateArray(), h => h.GetProperty("type").GetString() == "person");
    }

    [Fact]
    public async Task Search_hit_shape_includes_required_fields()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        var payload = await client.GetFromJsonAsync<JsonElement>("/api/search?q=");
        var hit = payload.GetProperty("hits").EnumerateArray().First();
        Assert.False(string.IsNullOrWhiteSpace(hit.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(hit.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(hit.GetProperty("label").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(hit.GetProperty("route").GetString()));
    }

    [Fact]
    public async Task Search_limit_is_honoured()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        for (var i = 0; i < 6; i++)
        {
            await client.PostAsJsonAsync("/api/projects", new { name = $"SearchProj{i}", slug = $"search-proj-{i}" });
        }

        var payload = await client.GetFromJsonAsync<JsonElement>("/api/search?q=SearchProj&limit=2");
        Assert.Equal(2, payload.GetProperty("hits").GetArrayLength());
    }

    private static WebApplicationFactory<Program> CreateEmptyHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-search-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-search-keys-{Guid.NewGuid():N}");
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

    private static async Task<HttpClient> BootstrapAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Search Org",
            organisationDescription = "Search tests",
            displayName = "Maya Owner",
            username = "maya",
            email = "maya@forgedeck.dev",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
