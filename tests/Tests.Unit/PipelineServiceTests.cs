using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;
using Modules.Pipelines.Infrastructure;
using Platform.Contracts.Events;

namespace Tests.Unit;

public sealed class PipelineServiceTests
{
    [Fact]
    public async Task Manual_run_executes_steps_sequentially_and_publishes_output()
    {
        var repository = new InMemoryPipelineRepository();
        var service = new PipelineService(repository, new DeterministicPipelineRunner());

        var run = await service.RunAsync(InMemoryPipelineRepository.BuildDefinitionId, PipelineTrigger.Manual, "main");

        Assert.Equal(PipelineRunStatus.Passed, run.Status);
        Assert.Equal(6, run.Logs.Count);
        Assert.Single(run.Outputs);
    }

    [Fact]
    public async Task Change_event_triggers_definitions_without_referencing_review_module()
    {
        var repository = new InMemoryPipelineRepository();
        var service = new PipelineService(repository, new DeterministicPipelineRunner());
        var handler = new ChangeOpenedPipelineTrigger(service, repository);
        var changeId = Guid.NewGuid();

        await handler.HandleAsync(new ChangeOpened(changeId, "ATL", "org/repo", "feature", "main", "abc1234"));

        Assert.Contains(repository.FindRunsForChange(changeId), run => run.Status == PipelineRunStatus.Passed);
    }

    [Fact]
    public async Task Native_push_event_triggers_pipeline_without_referencing_git_module()
    {
        var repository = new InMemoryPipelineRepository();
        var service = new PipelineService(repository, new DeterministicPipelineRunner());
        var handler = new PushReceivedPipelineTrigger(service, repository);

        await handler.HandleAsync(new PushReceived(Guid.NewGuid(), "ATL", "atlas-native", "main", "abc1234"));

        Assert.Contains(repository.ListRuns(), run => run.Trigger == PipelineTrigger.Push && run.CommitSha == "abc1234");
    }
}
