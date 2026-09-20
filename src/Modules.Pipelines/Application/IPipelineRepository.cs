using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Application;

public interface IPipelineRepository
{
    IReadOnlyList<PipelineDefinition> ListDefinitions();
    PipelineDefinition? FindDefinition(Guid id);
    void AddDefinition(PipelineDefinition definition);
    IReadOnlyList<PipelineRun> ListRuns();
    PipelineRun? FindRun(Guid id);
    IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId);
    void AddRun(PipelineRun run);
    void UpdateRun(PipelineRun run);
}
