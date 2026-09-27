using ForgeDeck.Build;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Domain;
using ForgeDeck.Build.Infrastructure;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Review.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Core.Tests;

public sealed class PipelineServiceTests
{
    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
            where TEvent : class =>
            Task.CompletedTask;
    }

    private sealed class CapturingPublisher : IEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
            where TEvent : class
        {
            Published.Add(data);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Manual_run_executes_jobs_sequentially_in_simulated_mode()
    {
        await using var harness = CreateHarness();
        var run = harness.Pipelines.StartRun(
            EfPipelineStore.DotNetValidationDefinitionId,
            PipelineTrigger.Manual,
            "main",
            "abc1234");

        await harness.Execution.DrainSimulatedAsync(run.Id);

        var completed = harness.Pipelines.FindRun(run.Id)!;
        Assert.Equal(PipelineRunStatus.Succeeded, completed.Status);
        Assert.Equal(4, completed.Jobs.Count);
        Assert.All(completed.Jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.Contains(completed.Jobs.SelectMany(j => j.Logs), log => log.Message.Contains("Completed successfully"));
    }

    [Fact]
    public async Task Review_requested_publishes_pipeline_run_requests_for_matching_definitions()
    {
        await using var harness = CreateHarness();
        var publisher = new CapturingPublisher();
        var trigger = new ReviewRequestedPipelineTrigger(harness.Store, publisher);
        var changeId = Guid.NewGuid();
        var envelope = Envelope(new ReviewRequestedEvent(changeId, "ATL", "org/repo", "feature", "main", "abc1234"));

        await trigger.HandleAsync(envelope);

        Assert.NotEmpty(publisher.Published.OfType<BuildPipelineRunRequestedEvent>());
        Assert.All(
            publisher.Published.OfType<BuildPipelineRunRequestedEvent>(),
            e => Assert.Equal(changeId, e.ChangeId));
    }

    [Fact]
    public async Task Pipeline_run_request_handler_creates_run_idempotently()
    {
        await using var harness = CreateHarness();
        var handler = new BuildPipelineRunRequestHandler(harness.Pipelines, harness.Store);
        var changeId = Guid.NewGuid();
        var definitionId = EfPipelineStore.DotNetValidationDefinitionId;
        var key = Guid.NewGuid();
        var request = new BuildPipelineRunRequestedEvent(
            definitionId,
            "DotNet Validation",
            "feature",
            "abc1234",
            changeId,
            Trigger: nameof(PipelineTrigger.ChangeOpened),
            IdempotencyKey: key);
        var envelope = Envelope(request);

        await handler.HandleAsync(envelope);
        await handler.HandleAsync(envelope);

        var runs = harness.Store.FindRunsForChange(changeId);
        Assert.Single(runs);
        Assert.Equal(key, runs[0].IdempotencyKey);
        await harness.Execution.DrainSimulatedAsync(runs[0].Id);
        Assert.Equal(PipelineRunStatus.Succeeded, harness.Pipelines.FindRun(runs[0].Id)!.Status);
    }

    [Fact]
    public async Task Git_push_publishes_pipeline_run_requests_for_push_triggers()
    {
        await using var harness = CreateHarness();
        var publisher = new CapturingPublisher();
        var trigger = new GitPushPipelineTrigger(harness.Store, publisher);
        var envelope = Envelope(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "atlas-native", "main", "abc1234"));

        await trigger.HandleAsync(envelope);

        Assert.Contains(
            publisher.Published.OfType<BuildPipelineRunRequestedEvent>(),
            e => e.Trigger == nameof(PipelineTrigger.Push) && e.CommitSha == "abc1234");
    }

    [Fact]
    public async Task Simulated_job_fails_when_command_contains_exit_1()
    {
        await using var harness = CreateHarness();
        var definition = harness.Pipelines.AddDefinition(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Failing",
            [PipelineTrigger.Manual],
            [
                new PipelineJobDefinition(
                    "Boom",
                    [new PipelineStepDefinition("Fail", "exit 1", null, new Dictionary<string, string>(), 60)],
                    [],
                    new Dictionary<string, string>(),
                    60,
                    PublishCheck: true,
                    CheckName: "Boom",
                    ArtifactGlobs: [])
            ]);

        var run = harness.Pipelines.StartRun(definition.Id, PipelineTrigger.Manual, "main", "deadbeef");
        await harness.Execution.DrainSimulatedAsync(run.Id);

        var completed = harness.Pipelines.FindRun(run.Id)!;
        Assert.Equal(PipelineRunStatus.Failed, completed.Status);
        Assert.Equal(JobStatus.Failed, completed.Jobs[0].Status);
    }

    [Fact]
    public async Task Manual_run_rejects_local_placeholder_sha()
    {
        var harness = CreateHarness();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                harness.Pipelines.StartRun(EfPipelineStore.DotNetValidationDefinitionId, PipelineTrigger.Manual, "main", "local"));
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    [Fact]
    public async Task Community_blocks_second_concurrent_pipeline_without_Build_Concurrent()
    {
        var harness = CreateHarness();
        try
        {
            harness.Pipelines.StartRun(
                EfPipelineStore.DotNetValidationDefinitionId,
                PipelineTrigger.Manual,
                "main",
                "abc1234");

            var ex = Assert.Throws<LicenceRequiredException>(() =>
                harness.Pipelines.StartRun(
                    EfPipelineStore.DotNetValidationDefinitionId,
                    PipelineTrigger.Manual,
                    "main",
                    "def5678"));
            Assert.Equal(KnownCapabilities.Build.Concurrent, ex.Capability);
            Assert.Equal(CommunityLimits.BuildMaxConcurrentPipelines, 1);
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    private static EventEnvelope<T> Envelope<T>(T data) where T : class =>
        new(
            Guid.NewGuid(),
            "test",
            1,
            DateTimeOffset.UtcNow,
            new EventActor(ActorType.System, "test", "test"),
            Guid.NewGuid(),
            null,
            null,
            null,
            "test",
            data);

    private static TestHarness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-pipelines-{Guid.NewGuid():N}.db");
        var factory = new TestBuildDbContextFactory($"Data Source={path}");
        var store = new EfPipelineStore(factory);
        var events = new NoopPublisher();
        var capabilities = new CapabilityService(
            [new BuildModule()],
            new SignedLicenceEntitlementStore((OrganisationLicenceEntitlement?)null));
        var context = new PlatformContextStore();
        var pipelines = new PipelineService(store, events, capabilities, context);
        var options = Options.Create(new PipelinesOptions { ExecutionMode = "Simulated" });
        var execution = new JobExecutionService(store, options, events);
        return new TestHarness(path, store, pipelines, execution);
    }

    private sealed class TestBuildDbContextFactory(string connectionString) : IDbContextFactory<BuildDbContext>
    {
        public BuildDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<BuildDbContext>().UseSqlite(connectionString).Options;
            return new BuildDbContext(options);
        }
    }

    private sealed class TestHarness(string path, IPipelineStore store, PipelineService pipelines, JobExecutionService execution) : IAsyncDisposable
    {
        public IPipelineStore Store { get; } = store;
        public PipelineService Pipelines { get; } = pipelines;
        public JobExecutionService Execution { get; } = execution;

        public ValueTask DisposeAsync()
        {
            try { File.Delete(path); } catch { /* ignore */ }
            return ValueTask.CompletedTask;
        }
    }
}
