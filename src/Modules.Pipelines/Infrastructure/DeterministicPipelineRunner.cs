using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Infrastructure;

public sealed class DeterministicPipelineRunner : IPipelineRunner
{
    public string Name => "Local MVP Runner";
    public Task ExecuteAsync(PipelineDefinition definition, PipelineRun run, CancellationToken cancellationToken = default)
    {
        run.Start();
        foreach (var step in definition.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.Log(step.Name, $"$ {step.Command}");
            if (step.Command.Contains("exit 1", StringComparison.OrdinalIgnoreCase))
            {
                run.Log(step.Name, "Command failed with exit code 1"); run.Complete(false); return Task.CompletedTask;
            }
            run.Log(step.Name, "Completed successfully");
        }
        run.Complete(true); return Task.CompletedTask;
    }
}
