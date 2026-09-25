using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class ExtensionLifecycleApiTests
{
    [Fact]
    public async Task Fresh_install_is_core_only_until_modules_are_installed()
    {
        await using var factory = CreateEmptyHost();
        using var client = await CreateOwnerClientAsync(factory);

        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.DoesNotContain("\"id\":\"review\"", modules);
        Assert.DoesNotContain("\"id\":\"pipelines\"", modules);
        Assert.DoesNotContain("\"id\":\"code\"", modules);

        var review = await client.GetAsync("/api/review/changes");
        Assert.Equal(HttpStatusCode.NotFound, review.StatusCode);

        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        Assert.Equal(JsonValueKind.Array, catalogue.ValueKind);
        Assert.Contains(catalogue.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.review");
    }

    [Fact]
    public async Task Install_enable_disable_review_controls_composition_and_api()
    {
        await using var factory = CreateEmptyHost();
        using var client = await CreateOwnerClientAsync(factory);

        var install = await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/install", new { enable = true });
        install.EnsureSuccessStatusCode();

        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.Contains("\"id\":\"review\"", modules);

        var changes = await client.GetAsync("/api/review/changes");
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);

        var disable = await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/disable", new { });
        disable.EnsureSuccessStatusCode();

        modules = await client.GetStringAsync("/api/platform/modules");
        Assert.DoesNotContain("\"id\":\"review\"", modules);

        changes = await client.GetAsync("/api/review/changes");
        Assert.Equal(HttpStatusCode.NotFound, changes.StatusCode);

        var enable = await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/enable", new { });
        enable.EnsureSuccessStatusCode();
        changes = await client.GetAsync("/api/review/changes");
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
    }

    [Fact]
    public async Task Connectors_catalogue_is_separate_from_modules()
    {
        await using var factory = CreateEmptyHost();
        using var client = await CreateOwnerClientAsync(factory);

        var modules = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        var connectors = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/connectors");

        Assert.DoesNotContain(modules.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.github");
        Assert.Contains(connectors.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.github");
        Assert.Contains(modules.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.code");
    }

    private static WebApplicationFactory<Program> CreateEmptyHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-ext-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-ext-keys-{Guid.NewGuid():N}");
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

    private static async Task<HttpClient> CreateOwnerClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Ext Labs",
            organisationDescription = "ext",
            displayName = "Owner",
            username = "owner",
            email = "owner@ext.dev",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }
}
