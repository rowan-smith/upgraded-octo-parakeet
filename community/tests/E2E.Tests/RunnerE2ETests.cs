using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace E2E.Tests;

public sealed class RunnerE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RunnerE2ETests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static IEnumerable<object[]> RunnerProfiles()
    {
        yield return ["e2e-linux", new[] { "dotnet" }, "linux"];
        yield return ["e2e-win", new[] { "dotnet", "docker" }, "windows"];
        yield return ["e2e-mac", new[] { "dotnet" }, "osx"];
        yield return ["e2e-node", new[] { "node" }, "linux"];
        yield return ["e2e-py", new[] { "python", "dotnet" }, "linux"];
        yield return ["e2e-pw", new[] { "playwright" }, "linux"];
        yield return ["e2e-gpu", new[] { "dotnet", "cuda" }, "linux"];
        yield return ["e2e-arm", new[] { "dotnet" }, "linux-arm64"];
    }

    [Theory]
    [MemberData(nameof(RunnerProfiles))]
    public async Task Register_and_list_runner_profile(string name, string[] capabilities, string os)
    {
        using var client = CreateClient();
        var (runnerId, token) = await RegisterAsync(client, name, capabilities, os);
        Assert.NotEqual(Guid.Empty, runnerId);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var list = await client.GetStringAsync("/api/pipelines/runners");
        Assert.Contains(name, list);
        foreach (var cap in capabilities)
        {
            Assert.Contains(cap, list);
        }
    }

    [Theory]
    [InlineData("Online")]
    [InlineData("Busy")]
    [InlineData("Idle")]
    public async Task Heartbeat_statuses_are_accepted(string status)
    {
        using var client = CreateClient();
        var (runnerId, token) = await RegisterAsync(client, $"hb-{status}", ["dotnet"], "linux");
        var response = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/heartbeat", token, new
        {
            status,
            capabilities = new[] { "dotnet" },
            currentJobCount = status == "Busy" ? 1 : 0,
            version = "9.9.9"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Runner_picks_up_waiting_job_and_completes_it()
    {
        using var client = CreateClient();
        var (runnerId, token) = await RegisterAsync(client, "worker", ["dotnet"], "linux");

        using var defs = JsonDocument.Parse(await client.GetStringAsync("/api/pipelines/definitions"));
        var definitionId = defs.RootElement[0].GetProperty("id").GetGuid();
        var runResponse = await client.PostAsJsonAsync("/api/pipelines/runs", new
        {
            definitionId,
            @ref = "main",
            commitSha = Guid.NewGuid().ToString("N")[..16],
            repositoryUrl = "https://github.com/rowan-smith/upgraded-octo-parakeet.git"
        });
        runResponse.EnsureSuccessStatusCode();
        using var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
        var runId = runDoc.RootElement.GetProperty("id").GetGuid();

        await WaitForAsync(client, $"/api/pipelines/runs/{runId}", "WaitingForRunner");

        var work = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/work", token, new { maxJobs = 1 });
        work.EnsureSuccessStatusCode();
        using var workDoc = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        Assert.True(workDoc.RootElement.GetArrayLength() > 0);
        var jobId = workDoc.RootElement[0].GetProperty("jobId").GetGuid();

        var complete = await RunnerPostAsync(client, $"/api/pipelines/runs/{runId}/jobs/complete", token, new
        {
            jobId,
            status = "Succeeded",
            exitCode = 0,
            failureReason = (string?)null,
            failureKind = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
    }

    [Theory]
    [InlineData("/api/pipelines/runners")]
    [InlineData("/api/pipelines/definitions")]
    [InlineData("/api/pipelines/runs")]
    public async Task Pipeline_get_routes_ok(string route)
    {
        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Revoke_runner_removes_it_from_list()
    {
        using var client = CreateClient();
        var (runnerId, _) = await RegisterAsync(client, "doomed", ["dotnet"], "linux");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/pipelines/runners/{runnerId}")).StatusCode);
        var list = await client.GetStringAsync("/api/pipelines/runners");
        Assert.DoesNotContain("doomed", list);
    }

    [Fact]
    public async Task Bad_registration_token_is_unauthorized()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/pipelines/runners/register", new
        {
            name = "x",
            registrationToken = "nope",
            operatingSystem = "linux",
            capabilities = new[] { "dotnet" },
            concurrency = 1,
            version = "1.0.0"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(Guid, string)> RegisterAsync(HttpClient client, string name, string[] capabilities, string os)
    {
        var tokenResponse = await client.PostAsJsonAsync("/api/pipelines/runners/registration-tokens", new { lifetimeHours = 1 });
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var registrationToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        var register = await client.PostAsJsonAsync("/api/pipelines/runners/register", new
        {
            name,
            registrationToken,
            operatingSystem = os,
            capabilities,
            concurrency = 2,
            version = "1.0.0"
        });
        register.EnsureSuccessStatusCode();
        using var reg = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        return (reg.RootElement.GetProperty("runnerId").GetGuid(), reg.RootElement.GetProperty("runnerToken").GetString()!);
    }

    private static async Task WaitForAsync(HttpClient client, string path, string needle, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var body = await client.GetStringAsync(path);
            if (body.Contains(needle, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(100);
        }
        throw new TimeoutException($"Timed out waiting for '{needle}' at {path}");
    }

    private static async Task<HttpResponseMessage> RunnerPostAsync(HttpClient client, string path, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body);
        request.Headers.Add("X-Runner-Token", token);
        return await client.SendAsync(request);
    }

    private HttpClient CreateClient()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-runner-e2e-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-keys-{Guid.NewGuid():N}");
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Runner");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Modules:pipelines:Enabled", "true");
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}

public sealed class HashRouteE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HashRouteE2ETests(WebApplicationFactory<Program> factory) => _factory = factory;

    public static IEnumerable<object[]> SpaShellRoutes()
    {
        string[] routes = ["/", "/index.html", "/app.js", "/app.css"];
        foreach (var route in routes)
        {
            yield return [route];
        }
    }

    [Theory]
    [MemberData(nameof(SpaShellRoutes))]
    public async Task Spa_static_assets_are_served(string route)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, $"{route} => {response.StatusCode}");
    }

    public static IEnumerable<object[]> ApiMatrix()
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
            "/api/source/repositories",
            "/api/users/me/starred-projects"
        ];
        foreach (var route in routes)
        {
            yield return [route];
        }
    }

    [Theory]
    [MemberData(nameof(ApiMatrix))]
    public async Task Authenticated_api_get_succeeds(string route)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(route);
        Assert.True(response.IsSuccessStatusCode, $"{route} => {response.StatusCode}");
    }

    private HttpClient CreateClient()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-hash-e2e-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-e2e-keys-{Guid.NewGuid():N}");
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
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
