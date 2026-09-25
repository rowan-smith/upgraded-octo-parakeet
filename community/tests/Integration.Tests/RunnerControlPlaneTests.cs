using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

/// <summary>Runner control-plane scenarios over HTTP (ExecutionMode=Runner, no simulated drain).</summary>
public sealed class RunnerControlPlaneTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RunnerControlPlaneTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Full_runner_lifecycle_register_heartbeat_work_complete_revoke()
    {
        using var client = AuthenticatedClient(CreateHost());
        var (runnerId, runnerToken) = await RegisterRunnerAsync(client, "lifecycle", ["dotnet"]);

        var heartbeat = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/heartbeat", runnerToken, new
        {
            status = "Online",
            capabilities = new[] { "dotnet" },
            currentJobCount = 0,
            version = "1.0.0",
            cpuCount = 4,
            memoryBytes = 8_000_000_000L,
            operatingSystem = "linux"
        });
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);

        var definitionId = await FirstDefinitionIdAsync(client);
        var run = await StartRunAsync(client, definitionId);
        var runId = run.GetProperty("id").GetGuid();

        // Wait until a job is WaitingForRunner (not Simulated)
        await WaitForJobStatusAsync(client, runId, "WaitingForRunner");

        var work = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/work", runnerToken, new { maxJobs = 1 });
        Assert.Equal(HttpStatusCode.OK, work.StatusCode);
        using var workDoc = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        Assert.True(workDoc.RootElement.GetArrayLength() >= 1);
        var assignment = workDoc.RootElement[0];
        var jobId = assignment.GetProperty("jobId").GetGuid();

        var logs = await RunnerPostAsync(client, $"/api/pipelines/runs/{runId}/jobs/{jobId}/logs", runnerToken, new[]
        {
            new { jobId, stepId = (Guid?)null, timestamp = DateTimeOffset.UtcNow, stream = "stdout", message = "hello from runner" }
        });
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);

        var complete = await RunnerPostAsync(client, $"/api/pipelines/runs/{runId}/jobs/complete", runnerToken, new
        {
            jobId,
            status = "Succeeded",
            exitCode = 0,
            failureReason = (string?)null,
            failureKind = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var revoke = await client.DeleteAsync($"/api/pipelines/runners/{runnerId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
    }

    [Theory]
    [InlineData("linux-ci", new[] { "dotnet" })]
    [InlineData("win-ci", new[] { "dotnet", "docker" })]
    [InlineData("node-ci", new[] { "node" })]
    [InlineData("py-ci", new[] { "python" })]
    [InlineData("pw-ci", new[] { "playwright", "dotnet" })]
    public async Task Can_register_runners_with_various_profiles(string name, string[] capabilities)
    {
        using var client = AuthenticatedClient(CreateHost());
        var (runnerId, _) = await RegisterRunnerAsync(client, name, capabilities);
        var list = await client.GetStringAsync("/api/pipelines/runners");
        Assert.Contains(name, list);
        Assert.Contains(runnerId.ToString(), list, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registration_token_cannot_be_reused()
    {
        using var client = AuthenticatedClient(CreateHost());
        var tokenResponse = await client.PostAsJsonAsync("/api/pipelines/runners/registration-tokens", new { lifetimeHours = 1 });
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var registrationToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        var first = await client.PostAsJsonAsync("/api/pipelines/runners/register", new
        {
            name = "one",
            registrationToken,
            operatingSystem = "linux",
            capabilities = new[] { "dotnet" },
            concurrency = 1,
            version = "1.0.0"
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/pipelines/runners/register", new
        {
            name = "two",
            registrationToken,
            operatingSystem = "linux",
            capabilities = new[] { "dotnet" },
            concurrency = 1,
            version = "1.0.0"
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Invalid_runner_token_is_unauthorized()
    {
        using var client = AuthenticatedClient(CreateHost());
        var (runnerId, _) = await RegisterRunnerAsync(client, "auth-test", ["dotnet"]);
        var response = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/heartbeat", "wrong-token", new
        {
            status = "Online",
            capabilities = new[] { "dotnet" },
            currentJobCount = 0,
            version = "1.0.0"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Work_request_is_empty_when_no_waiting_jobs()
    {
        using var client = AuthenticatedClient(CreateHost());
        var (runnerId, runnerToken) = await RegisterRunnerAsync(client, "idle", ["dotnet"]);
        var work = await RunnerPostAsync(client, $"/api/pipelines/runners/{runnerId}/work", runnerToken, new { maxJobs = 3 });
        Assert.Equal(HttpStatusCode.OK, work.StatusCode);
        using var doc = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(24)]
    [InlineData(168)]
    public async Task Registration_token_lifetime_hours_accepted(int hours)
    {
        using var client = AuthenticatedClient(CreateHost());
        var response = await client.PostAsJsonAsync("/api/pipelines/runners/registration-tokens", new { lifetimeHours = hours });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task List_runners_includes_registered_agent()
    {
        using var client = AuthenticatedClient(CreateHost());
        await RegisterRunnerAsync(client, "listed", ["dotnet"]);
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/pipelines/runners");
        Assert.Contains(list!, r => r.GetProperty("name").GetString() == "listed");
    }

    private async Task<(Guid RunnerId, string RunnerToken)> RegisterRunnerAsync(
        HttpClient client, string name, string[] capabilities)
    {
        var tokenResponse = await client.PostAsJsonAsync("/api/pipelines/runners/registration-tokens", new { lifetimeHours = 2 });
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var registrationToken = tokenDoc.RootElement.GetProperty("token").GetString()!;

        var register = await client.PostAsJsonAsync("/api/pipelines/runners/register", new
        {
            name,
            registrationToken,
            operatingSystem = "linux",
            capabilities,
            concurrency = 2,
            version = "1.2.3"
        });
        register.EnsureSuccessStatusCode();
        using var regDoc = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        return (
            regDoc.RootElement.GetProperty("runnerId").GetGuid(),
            regDoc.RootElement.GetProperty("runnerToken").GetString()!);
    }

    private static async Task<Guid> FirstDefinitionIdAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/pipelines/definitions"));
        return doc.RootElement.EnumerateArray().First().GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> StartRunAsync(HttpClient client, Guid definitionId)
    {
        var response = await client.PostAsJsonAsync("/api/pipelines/runs", new
        {
            definitionId,
            @ref = "main",
            commitSha = Guid.NewGuid().ToString("N")[..16],
            repositoryUrl = "https://github.com/rowan-smith/upgraded-octo-parakeet.git"
        });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task WaitForJobStatusAsync(HttpClient client, Guid runId, string status, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var payload = await client.GetStringAsync($"/api/pipelines/runs/{runId}");
            if (payload.Contains($"\"status\":\"{status}\"", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Run {runId} never reached job status {status}.");
    }

    private static async Task<HttpResponseMessage> RunnerPostAsync(
        HttpClient client, string path, string runnerToken, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Runner-Token", runnerToken);
        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-runner-api-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Pipelines:ExecutionMode", "Runner");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Modules:pipelines:Enabled", "true");
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mvp-admin-token");
        return client;
    }
}
