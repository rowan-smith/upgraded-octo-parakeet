using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

/// <summary>Deploy agent control-plane scenarios over HTTP (ExecutionMode=Agent).</summary>
public sealed class DeployAgentControlPlaneTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public DeployAgentControlPlaneTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Full_agent_lifecycle_register_claim_complete()
    {
        using var client = AuthenticatedClient(CreateHost());

        var envResponse = await client.PostAsJsonAsync("/api/deploy/environments", new { name = "production" });
        envResponse.EnsureSuccessStatusCode();
        using var envDoc = JsonDocument.Parse(await envResponse.Content.ReadAsStringAsync());
        var envId = envDoc.RootElement.GetProperty("id").GetGuid();

        var deploy = await client.PostAsJsonAsync("/api/deploy/deployments", new
        {
            environmentId = envId,
            version = "1.0.0",
            notes = "agent-queued"
        });
        Assert.Equal(HttpStatusCode.Created, deploy.StatusCode);
        using var depDoc = JsonDocument.Parse(await deploy.Content.ReadAsStringAsync());
        var deploymentId = depDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Pending", depDoc.RootElement.GetProperty("status").GetString());

        var (agentId, agentToken) = await RegisterAgentAsync(client, "deploy-ci");

        var heartbeat = await AgentPostAsync(client, $"/api/deploy/agents/{agentId}/heartbeat", agentToken, new
        {
            labels = new[] { "linux", "prod" }
        });
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);

        var work = await AgentPostAsync(client, $"/api/deploy/agents/{agentId}/work", agentToken, new { maxJobs = 1 });
        Assert.Equal(HttpStatusCode.OK, work.StatusCode);
        using var workDoc = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        Assert.True(workDoc.RootElement.GetArrayLength() >= 1);
        Assert.Equal(deploymentId, workDoc.RootElement[0].GetProperty("deploymentId").GetGuid());

        var complete = await AgentPostAsync(client, $"/api/deploy/agents/{agentId}/complete", agentToken, new
        {
            deploymentId,
            success = true,
            log = "deployed by agent"
        });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var get = await client.GetFromJsonAsync<JsonElement>($"/api/deploy/deployments/{deploymentId}");
        Assert.Equal("Succeeded", get.GetProperty("status").GetString());

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/deploy/agents");
        Assert.Contains(list!, a =>
            a.GetProperty("name").GetString() == "deploy-ci"
            && a.GetProperty("health").GetString() == "Healthy");
    }

    [Fact]
    public async Task Invalid_agent_token_is_unauthorized()
    {
        using var client = AuthenticatedClient(CreateHost());
        var (agentId, _) = await RegisterAgentAsync(client, "auth-test");
        var response = await AgentPostAsync(client, $"/api/deploy/agents/{agentId}/heartbeat", "wrong-token", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Work_request_is_empty_when_no_pending_deployments()
    {
        using var client = AuthenticatedClient(CreateHost());
        var (agentId, agentToken) = await RegisterAgentAsync(client, "idle");
        var work = await AgentPostAsync(client, $"/api/deploy/agents/{agentId}/work", agentToken, new { maxJobs = 3 });
        Assert.Equal(HttpStatusCode.OK, work.StatusCode);
        using var doc = JsonDocument.Parse(await work.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    private static async Task<(Guid AgentId, string AgentToken)> RegisterAgentAsync(HttpClient client, string name)
    {
        var register = await client.PostAsJsonAsync("/api/deploy/agents/register", new
        {
            name,
            labels = new[] { "linux" }
        });
        register.EnsureSuccessStatusCode();
        using var regDoc = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        return (
            regDoc.RootElement.GetProperty("agentId").GetGuid(),
            regDoc.RootElement.GetProperty("agentToken").GetString()!);
    }

    private static async Task<HttpResponseMessage> AgentPostAsync(
        HttpClient client, string path, string agentToken, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Deploy-Agent-Token", agentToken);
        return await client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateHost()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-deploy-agent-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-keys-{Guid.NewGuid():N}");
        var cs = $"Data Source={database}";
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Platform", cs);
            builder.UseSetting("ConnectionStrings:Deploy", cs);
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Deploy:ExecutionMode", "Agent");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Core:SeedDemoOnEmpty", "true");
            builder.UseSetting("Modules:deploy:Enabled", "true");
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        SeededAuth.AuthenticateClientAsync(client).GetAwaiter().GetResult();
        return client;
    }
}
