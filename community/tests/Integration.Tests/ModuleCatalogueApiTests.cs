using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

public sealed class ModuleCatalogueApiTests
{
    [Fact]
    public async Task Modules_catalogue_omits_uninstalled_team_and_enterprise_rows()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);

        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        var ids = catalogue.EnumerateArray().Select(e => e.GetProperty("extensionId").GetString()).ToArray();

        Assert.Contains("forgedeck.review", ids);
        Assert.DoesNotContain("forgedeck.review.team", ids);
        Assert.DoesNotContain("forgedeck.review.enterprise", ids);
        Assert.DoesNotContain("forgedeck.review.commercial", ids);
        Assert.DoesNotContain("forgedeck.build.team", ids);
        Assert.DoesNotContain("forgedeck.build.enterprise", ids);
        Assert.DoesNotContain("forgedeck.deploy.team", ids);
        Assert.DoesNotContain("forgedeck.deploy.enterprise", ids);
    }

    [Fact]
    public async Task Installing_community_review_does_not_surface_duplicate_team_row()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        (await client.PostAsJsonAsync("/api/platform/extensions/forgedeck.review/install", new { enable = true })).EnsureSuccessStatusCode();

        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        var reviewRows = catalogue.EnumerateArray()
            .Where(e => (e.GetProperty("extensionId").GetString() ?? "").StartsWith("forgedeck.review", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Single(reviewRows);
        Assert.Equal("forgedeck.review", reviewRows[0].GetProperty("extensionId").GetString());
        Assert.True(reviewRows[0].GetProperty("installed").GetBoolean());
    }

    [Fact]
    public async Task Available_modules_still_include_true_add_ons()
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        Assert.Contains(catalogue.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.code");
        Assert.Contains(catalogue.EnumerateArray(), e => e.GetProperty("extensionId").GetString() == "forgedeck.git"
            || e.GetProperty("runtimeId").GetString() == "git");
    }

    [Theory]
    [InlineData("forgedeck.review")]
    [InlineData("forgedeck.build")]
    [InlineData("forgedeck.deploy")]
    public async Task Base_module_rows_expose_edition_pill_field(string extensionId)
    {
        await using var host = CreateEmptyHost();
        using var client = await BootstrapAsync(host);
        var catalogue = await client.GetFromJsonAsync<JsonElement>("/api/platform/extensions/modules");
        var row = catalogue.EnumerateArray().Single(e => e.GetProperty("extensionId").GetString() == extensionId);
        Assert.True(row.TryGetProperty("edition", out var edition));
        Assert.False(string.IsNullOrWhiteSpace(edition.GetString()));
    }

    private static WebApplicationFactory<Program> CreateEmptyHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-modcat-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-modcat-keys-{Guid.NewGuid():N}");
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
            organisationName = "Module Org",
            displayName = "Owner",
            username = "owner",
            email = "owner@example.com",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await setup.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("token").GetString());
        return client;
    }
}
