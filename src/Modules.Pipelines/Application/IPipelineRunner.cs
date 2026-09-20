using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Application;

public interface IPipelineRunner
{
    string Name { get; }
    Task ExecuteAsync(PipelineDefinition definition, PipelineRun run, CancellationToken cancellationToken = default);
}
