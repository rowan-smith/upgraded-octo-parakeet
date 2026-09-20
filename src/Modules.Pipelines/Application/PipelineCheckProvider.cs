using Modules.Pipelines.Domain;
using Platform.Contracts.Checks;

namespace Modules.Pipelines.Application;

public sealed class PipelineCheckProvider(IPipelineRepository repository) : ICheckProvider
{
    public string Id => "pipelines";
    public Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default)
    {
        var checks = repository.FindRunsForChange(changeId)
            .GroupBy(run => run.DefinitionId).Select(group => group.First())
            .Select(run => new CheckResult("Pipelines", run.DefinitionName, Map(run.Status), Duration(run), $"#/runs/{run.Id}"))
            .ToArray();
        return Task.FromResult<IReadOnlyList<CheckResult>>(checks);
    }
    private static CheckStatus Map(PipelineRunStatus status) => status switch
    {
        PipelineRunStatus.Queued => CheckStatus.Queued, PipelineRunStatus.Running => CheckStatus.Running,
        PipelineRunStatus.Passed => CheckStatus.Passed, _ => CheckStatus.Failed
    };
    private static string Duration(PipelineRun run) => run.StartedAt is null ? "—" : ((run.CompletedAt ?? DateTimeOffset.UtcNow) - run.StartedAt.Value).TotalSeconds.ToString("0.0s");
}
