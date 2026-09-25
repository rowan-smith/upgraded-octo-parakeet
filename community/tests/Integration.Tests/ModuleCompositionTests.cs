using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Integration.Tests;

public sealed class ModuleCompositionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ModuleCompositionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Enabled_review_module_registers_navigation_and_routes()
    {
        using var client = AuthenticatedClient(CreateHost());
        var modules = await client.GetStringAsync("/api/platform/modules");
        var changes = await client.GetAsync("/api/review/changes");

        Assert.Contains("\"id\":\"review\"", modules);
        Assert.Contains("/changes", modules);
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
    }

    [Fact]
    public async Task Disabled_review_module_removes_navigation_and_routes()
    {
        using var client = AuthenticatedClient(CreateHost("Disabled"));
        var modules = await client.GetStringAsync("/api/platform/modules");
        var changes = await client.GetAsync("/api/review/changes");

        Assert.DoesNotContain("\"id\":\"review\"", modules);
        Assert.Equal(HttpStatusCode.NotFound, changes.StatusCode);
    }

    [Fact]
    public async Task Imported_change_triggers_pipeline_and_exposes_check_through_contract()
    {
        using var client = AuthenticatedClient(CreateHost("Full"));
        var modules = await client.GetStringAsync("/api/platform/modules");
        Assert.Contains("\"id\":\"pipelines\"", modules);

        var externalId = Random.Shared.Next(1000, 9999).ToString();
        var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
        {
            providerId = "native-git",
            repositoryUrl = "native://northstar/atlas-native",
            externalId
        });
        imported.EnsureSuccessStatusCode();
        using var change = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
        var changeId = change.RootElement.GetProperty("id").GetGuid();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        string checks = "";
        while (DateTime.UtcNow < deadline)
        {
            checks = await client.GetStringAsync($"/api/review/changes/{changeId}/checks");
            if (checks.Contains("Passed", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.True(
            checks.Contains("Build", StringComparison.Ordinal) || checks.Contains("Unit Tests", StringComparison.Ordinal),
            $"Expected Build or Unit Tests check, got: {checks}");
        Assert.Contains("Passed", checks);
    }

    [Fact]
    public async Task Review_operates_without_pipelines_and_has_no_checks_extension()
    {
        using var client = AuthenticatedClient(CreateHost("ReviewOnly"));
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
        using var client = AuthenticatedClient(CreateHost("Full"));
        var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
        {
            providerId = "native-git",
            repositoryUrl = "native://northstar/atlas-native",
            externalId = Random.Shared.Next(1000, 9999).ToString()
        });
        imported.EnsureSuccessStatusCode();
        Assert.Contains("Native change #", await imported.Content.ReadAsStringAsync());

        // Community soft-limit: wait for the import-triggered run to leave Queued/Running.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var active = await client.GetStringAsync("/api/pipelines/runs");
            if (!active.Contains("\"status\":\"Queued\"", StringComparison.Ordinal)
                && !active.Contains("\"status\":\"Running\"", StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(200);
        }

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
        using var client = AuthenticatedClient(CreateHost("GitOnly"));
        var modules = await client.GetStringAsync("/api/platform/modules");
        var repositories = await client.GetAsync("/api/git/repositories");
        var changes = await client.GetAsync("/api/review/changes");
        var runs = await client.GetAsync("/api/pipelines/runs");

        Assert.Contains("\"id\":\"git\"", modules);
        Assert.Equal(HttpStatusCode.OK, repositories.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, changes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, runs.StatusCode);
    }

    [Fact]
    public async Task Local_repository_can_be_detected_and_associated()
    {
        using var client = AuthenticatedClient(CreateHost());
        var root = FindRepositoryRoot();
        var detected = await client.PostAsJsonAsync("/api/projects/current/local-repository/detect", new { path = root });
        detected.EnsureSuccessStatusCode();
        var payload = await detected.Content.ReadAsStringAsync();
        Assert.Contains("\"isGitRepository\":true", payload);

        var associated = await client.PostAsJsonAsync("/api/projects/current/local-repository", new { path = root, connectGitHub = false });
        associated.EnsureSuccessStatusCode();
        var current = await client.GetStringAsync("/api/projects/current/local-repository");
        Assert.Contains("\"associated\":true", current);
        Assert.Contains("upgraded-octo-parakeet", current, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Review_change_supports_comments_reviews_and_merge_policy()
    {
        using var client = AuthenticatedClient(CreateHost("Full"));
        var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
        {
            providerId = "native-git",
            repositoryUrl = "native://northstar/atlas-native",
            externalId = Random.Shared.Next(1000, 9999).ToString()
        });
        imported.EnsureSuccessStatusCode();
        using var changeDoc = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
        var changeId = changeDoc.RootElement.GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/review/changes/{changeId}/comments", new { body = "General feedback" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/review/changes/{changeId}/comments", new
        {
            body = "Inline note",
            file = "src/Native/Hooks.cs",
            side = "right",
            line = 3,
            commitSha = changeDoc.RootElement.GetProperty("headCommit").GetString()
        })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/review/changes/{changeId}/reviewers", new { name = "Maya Chen" })).EnsureSuccessStatusCode();

        // Full environment requires pipeline checks; wait for simulated runner to finish.
        await WaitForChecksAsync(client, changeId, "Passed");

        var review = await client.PostAsJsonAsync($"/api/review/changes/{changeId}/reviews", new { state = "Approved", body = "LGTM" });
        review.EnsureSuccessStatusCode();
        using var approved = JsonDocument.Parse(await review.Content.ReadAsStringAsync());
        Assert.True(approved.RootElement.GetProperty("canMerge").GetBoolean());

        var merge = await client.PostAsJsonAsync($"/api/review/changes/{changeId}/merge", new { });
        merge.EnsureSuccessStatusCode();
        Assert.Contains("Merged", await merge.Content.ReadAsStringAsync());
    }

    private static async Task WaitForChecksAsync(HttpClient client, Guid changeId, string expectedStatus, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var checks = await client.GetStringAsync($"/api/review/changes/{changeId}/checks");
            if (checks.Contains(expectedStatus, StringComparison.OrdinalIgnoreCase)
                && checks.Contains("Build", StringComparison.OrdinalIgnoreCase)
                && checks.Contains("E2E Tests", StringComparison.OrdinalIgnoreCase)
                && !checks.Contains("\"status\":\"Queued\"", StringComparison.OrdinalIgnoreCase)
                && !checks.Contains("\"status\":\"Running\"", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(200);
        }

        var final = await client.GetStringAsync($"/api/review/changes/{changeId}/checks");
        throw new TimeoutException($"Checks did not reach a terminal state in time. Last payload: {final}");
    }

    private WebApplicationFactory<Program> CreateHost(string? environment = null)
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-tests-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment is "Disabled" or "ReviewOnly" or "GitOnly" or "Full"
                ? "Development"
                : environment ?? "Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");

            // Former appsettings.{Full,ReviewOnly,GitOnly,Disabled}.json profiles → explicit module gates.
            switch (environment)
            {
                case "Disabled":
                    builder.UseSetting("Modules:review:Enabled", "false");
                    builder.UseSetting("Modules:pipelines:Enabled", "false");
                    builder.UseSetting("Modules:deploy:Enabled", "false");
                    builder.UseSetting("Modules:git:Enabled", "false");
                    break;
                case "ReviewOnly":
                    builder.UseSetting("Modules:review:Enabled", "true");
                    builder.UseSetting("Modules:pipelines:Enabled", "false");
                    builder.UseSetting("Modules:deploy:Enabled", "false");
                    builder.UseSetting("Modules:git:Enabled", "false");
                    break;
                case "GitOnly":
                    builder.UseSetting("Modules:git:Enabled", "true");
                    builder.UseSetting("Modules:review:Enabled", "false");
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

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the git repository root for the local-repository test.");
    }
}
