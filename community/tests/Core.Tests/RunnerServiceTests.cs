using ForgeDeck.Build;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Domain;
using ForgeDeck.Build.Infrastructure;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Integrations;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Pipelines;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Core.Tests;

public sealed class RunnerServiceTests
{
    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent =>
            Task.CompletedTask;
    }

    private sealed class FakeCredentials : IProviderCredentialStore
    {
        public string? Token { get; set; } = "ghp_test";
        public string? Get(string providerId) => providerId == "github" ? Token : null;
        public bool IsConfigured(string providerId) => Get(providerId) is not null;
        public void Set(string providerId, string secret) => Token = secret;
        public void Delete(string providerId) => Token = null;
    }

    [Fact]
    public void Registration_token_registers_runner_once()
    {
        using var harness = CreateHarness();
        var (raw, _) = harness.Runners.CreateRegistrationToken("maya@test");
        var response = harness.Runners.Register(new RunnerRegistrationRequest(
            "linux-1", raw, "linux", ["dotnet"], 1, "1.0.0"));

        Assert.NotEqual(Guid.Empty, response.RunnerId);
        Assert.False(string.IsNullOrWhiteSpace(response.RunnerToken));
        Assert.Contains("WARNING", response.Warning);
        Assert.Single(harness.Runners.ListRunners());
        Assert.Equal(RunnerStatus.Online, harness.Runners.ListRunners()[0].Status);

        Assert.Throws<InvalidOperationException>(() =>
            harness.Runners.Register(new RunnerRegistrationRequest("linux-2", raw, "linux", ["dotnet"], 1, "1.0.0")));
    }

    [Fact]
    public void Invalid_registration_token_is_rejected()
    {
        using var harness = CreateHarness();
        Assert.Throws<UnauthorizedAccessException>(() =>
            harness.Runners.Register(new RunnerRegistrationRequest(
                "x", "not-a-real-token", "linux", ["dotnet"], 1, "1.0.0")));
    }

    [Fact]
    public void Expired_registration_token_is_rejected()
    {
        using var harness = CreateHarness();
        var (raw, _) = harness.Runners.CreateRegistrationToken("maya", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(50);
        Assert.Throws<InvalidOperationException>(() =>
            harness.Runners.Register(new RunnerRegistrationRequest("x", raw, "linux", ["dotnet"], 1, "1.0.0")));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void Concurrency_is_clamped_to_at_least_one(int requested, int expected)
    {
        using var harness = CreateHarness();
        var (raw, _) = harness.Runners.CreateRegistrationToken("maya");
        harness.Runners.Register(new RunnerRegistrationRequest("r", raw, "linux", ["dotnet"], requested, "1.0.0"));
        Assert.Equal(expected, harness.Runners.ListRunners()[0].Concurrency);
    }

    [Theory]
    [InlineData("Online")]
    [InlineData("Busy")]
    [InlineData("Idle")]
    [InlineData("offline")]
    public void Heartbeat_updates_status_and_metadata(string status)
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"]);
        var response = harness.Runners.Heartbeat(runnerId, token, new RunnerHeartbeatRequest(
            status, ["dotnet", "docker"], 1, "2.0.0", 8, 16_000_000_000, "linux"));
        Assert.Empty(response.CancelJobIds);

        var runner = harness.Runners.ListRunners().Single();
        Assert.Equal(["dotnet", "docker"], runner.Capabilities);
        Assert.Equal(1, runner.CurrentJobCount);
        Assert.Equal("2.0.0", runner.Version);
        Assert.Equal(8, runner.CpuCount);
        Assert.Equal(16_000_000_000, runner.MemoryBytes);
        Assert.NotNull(runner.LastHeartbeatAt);
    }

    [Fact]
    public void Heartbeat_with_bad_token_fails()
    {
        using var harness = CreateHarness();
        var (runnerId, _) = Register(harness, ["dotnet"]);
        Assert.Throws<UnauthorizedAccessException>(() =>
            harness.Runners.Heartbeat(runnerId, "bad-token", new RunnerHeartbeatRequest("Online", ["dotnet"], 0, "1.0.0")));
    }

    [Fact]
    public void Request_work_assigns_waiting_job_matching_capabilities()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"]);
        var run = StartWaitingRun(harness, requires: ["dotnet"]);

        var assignments = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1));
        Assert.Single(assignments);
        Assert.Equal(run.Id, assignments[0].RunId);
        Assert.Equal("dotnet", assignments[0].RequiredCapabilities.Single());
        Assert.Contains("FORGEDECK_RUN_ID", assignments[0].Environment.Keys);
        Assert.Equal(JobStatus.Assigned, harness.Pipelines.FindRun(run.Id)!.Jobs[0].Status);
    }

    [Fact]
    public void Request_work_skips_jobs_when_capabilities_do_not_match()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["node"]);
        StartWaitingRun(harness, requires: ["dotnet"]);

        var assignments = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(5));
        Assert.Empty(assignments);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("docker")]
    [InlineData("playwright")]
    [InlineData("python")]
    [InlineData("node")]
    public void Capability_match_is_case_insensitive(string capability)
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, [capability.ToUpperInvariant()]);
        StartWaitingRun(harness, requires: [capability.ToLowerInvariant()]);
        Assert.Single(harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1)));
    }

    [Fact]
    public void Request_work_respects_concurrency_slots()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"], concurrency: 1);
        StartWaitingRun(harness, requires: ["dotnet"], name: "One");
        StartWaitingRun(harness, requires: ["dotnet"], name: "Two");

        Assert.Single(harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(5)));
        Assert.Empty(harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(5)));
    }

    [Fact]
    public void Request_work_respects_max_jobs()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"], concurrency: 5);
        StartWaitingRun(harness, requires: ["dotnet"], name: "A");
        // Second run needs first job completed for sequential pipeline — start two definitions instead
        StartWaitingRun(harness, requires: ["dotnet"], name: "B");

        var first = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1));
        Assert.Single(first);
        var second = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1));
        Assert.Single(second);
        Assert.NotEqual(first[0].RunId, second[0].RunId);
    }

    [Fact]
    public void Complete_job_frees_runner_slot_for_next_assignment()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"], concurrency: 1);
        var run1 = StartWaitingRun(harness, requires: ["dotnet"], name: "First");
        var run2 = StartWaitingRun(harness, requires: ["dotnet"], name: "Second");

        var a1 = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1));
        Assert.Single(a1);
        harness.Execution.CompleteJob(a1[0].RunId, new JobCompleteDto(a1[0].JobId, "Succeeded", 0, null, null));

        var a2 = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1));
        Assert.Single(a2);
        Assert.Contains(new[] { run1.Id, run2.Id }, id => id == a2[0].RunId);
    }

    [Fact]
    public void Cancelled_job_is_surfaced_on_heartbeat()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"]);
        var run = StartWaitingRun(harness, requires: ["dotnet"]);
        var assignment = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1)).Single();

        harness.Pipelines.CancelRun(run.Id, "stop");
        var heartbeat = harness.Runners.Heartbeat(runnerId, token, new RunnerHeartbeatRequest(
            "Busy", ["dotnet"], 1, "1.0.0"));
        Assert.Contains(assignment.JobId, heartbeat.CancelJobIds);

        harness.Runners.Heartbeat(runnerId, token, new RunnerHeartbeatRequest(
            "Online", ["dotnet"], 0, "1.0.0", AcknowledgedCancelJobIds: [assignment.JobId]));
        var afterAck = harness.Runners.Heartbeat(runnerId, token, new RunnerHeartbeatRequest(
            "Online", ["dotnet"], 0, "1.0.0"));
        Assert.DoesNotContain(assignment.JobId, afterAck.CancelJobIds);
    }

    [Fact]
    public void Revoke_removes_runner_and_blocks_auth()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"]);
        harness.Runners.Revoke(runnerId);
        Assert.Empty(harness.Runners.ListRunners());
        Assert.Throws<UnauthorizedAccessException>(() =>
            harness.Runners.Authenticate(runnerId, token));
    }

    [Fact]
    public void Revoke_missing_runner_throws()
    {
        using var harness = CreateHarness();
        Assert.Throws<KeyNotFoundException>(() => harness.Runners.Revoke(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("agent-a")]
    [InlineData("windows-build")]
    [InlineData("gpu-runner")]
    [InlineData("ci-1")]
    [InlineData("ci-2")]
    public void Multiple_named_runners_can_register(string name)
    {
        using var harness = CreateHarness();
        var (raw, _) = harness.Runners.CreateRegistrationToken("maya");
        var response = harness.Runners.Register(new RunnerRegistrationRequest(
            name, raw, "linux", ["dotnet"], 1, "1.0.0"));
        Assert.Equal(name, harness.Store.FindRunner(response.RunnerId)!.Name);
    }

    [Fact]
    public void Clone_token_from_credentials_is_included_in_assignment()
    {
        using var harness = CreateHarness();
        harness.Credentials.Token = "secret-clone";
        var (runnerId, token) = Register(harness, ["dotnet"]);
        StartWaitingRun(harness, requires: ["dotnet"]);
        var assignment = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1)).Single();
        Assert.Equal("secret-clone", assignment.CloneToken);
    }

    [Fact]
    public void Empty_required_capabilities_match_any_runner()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["anything"]);
        StartWaitingRun(harness, requires: []);
        Assert.Single(harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(1)));
    }

    [Fact]
    public void Request_work_with_zero_available_slots_returns_empty()
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"], concurrency: 1);
        harness.Runners.Heartbeat(runnerId, token, new RunnerHeartbeatRequest(
            "Busy", ["dotnet"], 1, "1.0.0"));
        StartWaitingRun(harness, requires: ["dotnet"]);
        Assert.Empty(harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(5)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MaxJobs_limits_batch_size(int maxJobs)
    {
        using var harness = CreateHarness();
        var (runnerId, token) = Register(harness, ["dotnet"], concurrency: 10);
        for (var i = 0; i < 5; i++)
        {
            StartWaitingRun(harness, requires: ["dotnet"], name: $"Def{i}");
        }

        var batch = harness.Runners.RequestWork(runnerId, token, new RunnerWorkRequest(maxJobs));
        Assert.Equal(maxJobs, batch.Count);
    }

    private static (Guid RunnerId, string Token) Register(Harness harness, IReadOnlyList<string> capabilities, int concurrency = 1)
    {
        var (raw, _) = harness.Runners.CreateRegistrationToken("maya@forgedeck.dev");
        var response = harness.Runners.Register(new RunnerRegistrationRequest(
            "test-runner", raw, "linux", capabilities, concurrency, "1.0.0"));
        return (response.RunnerId, response.RunnerToken);
    }

    private static PipelineRun StartWaitingRun(Harness harness, IReadOnlyList<string> requires, string name = "Job")
    {
        var definition = harness.Pipelines.AddDefinition(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            $"pipe-{Guid.NewGuid():N}"[..16],
            [PipelineTrigger.Manual],
            [
                new PipelineJobDefinition(
                    name,
                    [new PipelineStepDefinition("step", "echo hi", null, new Dictionary<string, string>(), 60)],
                    requires,
                    new Dictionary<string, string>(),
                    60,
                    PublishCheck: false,
                    CheckName: name,
                    ArtifactGlobs: [])
            ]);
        var run = harness.Pipelines.StartRun(definition.Id, PipelineTrigger.Manual, "main", Guid.NewGuid().ToString("N")[..12]);
        // Ensure first job is waiting (StartRun should do this)
        Assert.Contains(run.Jobs, j => j.Status is JobStatus.WaitingForRunner or JobStatus.Queued);
        if (run.Jobs[0].Status == JobStatus.Queued)
        {
            run.Jobs[0].MarkWaitingForRunner();
            harness.Store.SaveRun(run);
        }
        return harness.Pipelines.FindRun(run.Id)!;
    }

    private static Harness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-runners-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={path}");
        var schema = new PipelineSchemaInitializer(connections);
        var store = new SqlitePipelineStore(connections, schema);
        var events = new NoopPublisher();
        var capabilities = new CapabilityService(
            [new BuildModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        // Soft concurrent limit: grant Concurrent via fake Team package so multi-run scenarios work.
        var team = new FakeBuildTeamModule();
        var keys = LicenceCryptography.CreateKeyPair();
        var document = LicenceSkuCatalog.CreateDocument("BUILD-TEAM", KnownIds.OrganisationId);
        document.Signature = LicenceCryptography.Sign(document, keys);
        var json = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        var entitlement = SignedLicenceEntitlementStore.ParseVerified(json, keys)!;
        capabilities = new CapabilityService(
            [new BuildModule(), team],
            new SignedLicenceEntitlementStore(entitlement));

        var context = new PlatformContextStore();
        var pipelines = new PipelineService(store, events, capabilities, context);
        var credentials = new FakeCredentials();
        var runners = new RunnerService(store, credentials, context);
        var options = Options.Create(new PipelinesOptions { ExecutionMode = "Runner" });
        var execution = new JobExecutionService(store, options, events);
        return new Harness(path, store, pipelines, runners, execution, credentials);
    }

    private sealed class FakeBuildTeamModule : ForgeDeck.Contracts.Modules.IPlatformModule
    {
        public ModuleManifest Manifest { get; } = new(
            "build-team", "Build Team", "0.1.0", "Team",
            [KnownCapabilities.Build.Concurrent], [], []);

        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration) { }
        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) { }
    }

    private sealed class Harness(
        string path,
        IPipelineStore store,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution,
        FakeCredentials credentials) : IDisposable
    {
        public IPipelineStore Store { get; } = store;
        public PipelineService Pipelines { get; } = pipelines;
        public RunnerService Runners { get; } = runners;
        public JobExecutionService Execution { get; } = execution;
        public FakeCredentials Credentials { get; } = credentials;

        public void Dispose()
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
