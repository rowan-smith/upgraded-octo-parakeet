using Microsoft.Extensions.Options;
using Modules.Pipelines;
using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;
using Modules.Pipelines.Infrastructure;
using Platform.Contracts.Events;
using Platform.Core.Persistence;

namespace Tests.Unit;

public sealed class PipelineServiceTests
{
    private sealed class NoopPublisher : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task Manual_run_executes_jobs_sequentially_in_simulated_mode()
    {
        await using var harness = CreateHarness();
        var run = harness.Pipelines.StartRun(
            SqlitePipelineStore.DotNetValidationDefinitionId,
            PipelineTrigger.Manual,
            "main",
            "abc1234");

        await harness.Execution.DrainSimulatedAsync(run.Id);

        var completed = harness.Pipelines.FindRun(run.Id)!;
        Assert.Equal(PipelineRunStatus.Succeeded, completed.Status);
        Assert.Equal(5, completed.Jobs.Count);
        Assert.All(completed.Jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
        Assert.Contains(completed.Jobs.SelectMany(j => j.Logs), log => log.Message.Contains("Completed successfully"));
    }

    [Fact]
    public async Task Change_event_triggers_definitions_without_referencing_review_module()
    {
        await using var harness = CreateHarness();
        var handler = new ChangeOpenedPipelineTrigger(harness.Pipelines, harness.Store);
        var changeId = Guid.NewGuid();

        await handler.HandleAsync(new ChangeOpened(changeId, "ATL", "org/repo", "feature", "main", "abc1234"));

        var runs = harness.Store.FindRunsForChange(changeId);
        Assert.NotEmpty(runs);
        await harness.Execution.DrainSimulatedAsync(runs[0].Id);
        Assert.Contains(harness.Store.FindRunsForChange(changeId), run => run.Status == PipelineRunStatus.Succeeded);
    }

    [Fact]
    public async Task Native_push_event_triggers_pipeline_without_referencing_git_module()
    {
        await using var harness = CreateHarness();
        var handler = new PushReceivedPipelineTrigger(harness.Pipelines, harness.Store);

        await handler.HandleAsync(new PushReceived(Guid.NewGuid(), "ATL", "atlas-native", "main", "abc1234"));

        var runs = harness.Pipelines.ListRuns();
        Assert.Contains(runs, run => run.Trigger == PipelineTrigger.Push && run.CommitSha == "abc1234");
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
    public void Manual_run_rejects_local_placeholder_sha()
    {
        var harness = CreateHarness();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                harness.Pipelines.StartRun(SqlitePipelineStore.DotNetValidationDefinitionId, PipelineTrigger.Manual, "main", "local"));
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static TestHarness CreateHarness()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgedeck-pipelines-{Guid.NewGuid():N}.db");
        var connections = new SqliteConnectionFactory($"Data Source={path}");
        var schema = new PipelineSchemaInitializer(connections);
        var store = new SqlitePipelineStore(connections, schema);
        var events = new NoopPublisher();
        var pipelines = new PipelineService(store, events);
        var options = Options.Create(new PipelinesOptions { ExecutionMode = "Simulated" });
        var execution = new JobExecutionService(store, options, events);
        return new TestHarness(path, store, pipelines, execution);
    }

    private sealed class TestHarness(string path, IPipelineStore store, PipelineService pipelines, JobExecutionService execution) : IAsyncDisposable
    {
        public IPipelineStore Store { get; } = store;
        public PipelineService Pipelines { get; } = pipelines;
        public JobExecutionService Execution { get; } = execution;

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // best-effort cleanup
            }
            return ValueTask.CompletedTask;
        }
    }
}
