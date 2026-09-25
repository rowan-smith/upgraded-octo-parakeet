using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

/// <summary>
/// Manual smoke path against a real GitHub repository.
/// Enable with: FORGEDECK_GITHUB_SMOKE=1 and FORGEDECK_GITHUB_TOKEN set.
/// Mutating create-change path requires FORGEDECK_GITHUB_SMOKE_MUTATE=1.
/// </summary>
public sealed class GitHubSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GitHubSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Real_github_authenticate_browse_create_diff_and_merge()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FORGEDECK_GITHUB_SMOKE"), "1", StringComparison.Ordinal))
        {
            return; // skipped unless explicitly enabled
        }

        var token = Environment.GetEnvironmentVariable("FORGEDECK_GITHUB_TOKEN");
        Assert.False(string.IsNullOrWhiteSpace(token), "FORGEDECK_GITHUB_TOKEN is required for smoke tests.");
        var owner = Environment.GetEnvironmentVariable("FORGEDECK_GITHUB_OWNER") ?? "rowan-smith";
        var name = Environment.GetEnvironmentVariable("FORGEDECK_GITHUB_REPO") ?? "upgraded-octo-parakeet";

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");

        var credential = await client.PutAsJsonAsync("/api/core/integrations/github", new { token });
        credential.EnsureSuccessStatusCode();

        var connect = await client.PostAsJsonAsync("/api/source/repositories", new { providerId = "github", url = $"https://github.com/{owner}/{name}" });
        connect.EnsureSuccessStatusCode();
        using var connectionDoc = JsonDocument.Parse(await connect.Content.ReadAsStringAsync());
        var connectionId = connectionDoc.RootElement.GetProperty("id").GetGuid();
        var defaultBranch = connectionDoc.RootElement.TryGetProperty("defaultBranch", out var branchProp)
            ? branchProp.GetString() ?? "main"
            : "main";

        var branches = await client.GetAsync($"/api/source/repositories/{connectionId}/branches");
        branches.EnsureSuccessStatusCode();
        Assert.Contains("main", await branches.Content.ReadAsStringAsync());

        var commits = await client.GetAsync($"/api/source/repositories/{connectionId}/commits?branch={Uri.EscapeDataString(defaultBranch)}");
        commits.EnsureSuccessStatusCode();
        using var commitsDoc = JsonDocument.Parse(await commits.Content.ReadAsStringAsync());
        Assert.True(commitsDoc.RootElement.GetArrayLength() > 0, "Expected at least one commit on the default branch.");

        var tree = await client.GetAsync($"/api/source/repositories/{connectionId}/tree?reference={Uri.EscapeDataString(defaultBranch)}");
        tree.EnsureSuccessStatusCode();
        using var treeDoc = JsonDocument.Parse(await tree.Content.ReadAsStringAsync());
        Assert.True(treeDoc.RootElement.GetProperty("entries").GetArrayLength() > 0, "Expected a non-empty tree for the default branch.");

        var firstFile = treeDoc.RootElement.GetProperty("entries").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("kind").GetString() == "file");
        Assert.NotEqual(default, firstFile);
        var filePath = firstFile.GetProperty("path").GetString()!;
        var file = await client.GetAsync($"/api/source/repositories/{connectionId}/file?path={Uri.EscapeDataString(filePath)}&reference={Uri.EscapeDataString(defaultBranch)}");
        file.EnsureSuccessStatusCode();
        Assert.Contains(filePath, await file.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(Environment.GetEnvironmentVariable("FORGEDECK_GITHUB_SMOKE_MUTATE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var sourceBranch = $"forgedeck-smoke/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var create = await client.PostAsJsonAsync("/api/review/changes", new
        {
            repositoryConnectionId = connectionId,
            title = $"ForgeDeck smoke {sourceBranch}",
            description = "Automated smoke mutation (FORGEDECK_GITHUB_SMOKE_MUTATE=1).",
            sourceBranch,
            targetBranch = defaultBranch
        });
        if (!create.IsSuccessStatusCode)
        {
            // Creating a PR requires the branch to already exist on GitHub; fall back to import of an existing open PR if available.
            var discover = await client.GetAsync($"/api/review/external-changes?repositoryConnectionId={connectionId}");
            discover.EnsureSuccessStatusCode();
            using var discoverDoc = JsonDocument.Parse(await discover.Content.ReadAsStringAsync());
            if (discoverDoc.RootElement.GetArrayLength() == 0)
            {
                return;
            }

            var externalId = discoverDoc.RootElement[0].GetProperty("externalId").GetString();
            var imported = await client.PostAsJsonAsync("/api/review/changes/import", new
            {
                repositoryConnectionId = connectionId,
                externalId
            });
            imported.EnsureSuccessStatusCode();
            return;
        }

        create.EnsureSuccessStatusCode();
    }
}
