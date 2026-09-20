using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Application;

public sealed class PipelineService(IPipelineRepository repository, IPipelineRunner runner)
{
    public IReadOnlyList<PipelineDefinition> ListDefinitions() => repository.ListDefinitions();
    public IReadOnlyList<PipelineRun> ListRuns() => repository.ListRuns();
    public PipelineRun? FindRun(Guid id) => repository.FindRun(id);
    public PipelineDefinition AddDefinition(string name, IReadOnlyList<PipelineTrigger> triggers, IReadOnlyList<PipelineStep> steps, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (string.IsNullOrWhiteSpace(name) || steps.Count == 0) throw new ArgumentException("A pipeline needs a name and at least one step.");
        var definition = new PipelineDefinition { Name = name.Trim(), Triggers = triggers, Steps = steps, Environment = environment ?? new Dictionary<string, string>() };
        repository.AddDefinition(definition); return definition;
    }
    public async Task<PipelineRun> RunAsync(Guid definitionId, PipelineTrigger trigger, string reference, Guid? changeId = null, string? commitSha = null, CancellationToken token = default)
    {
        var definition = repository.FindDefinition(definitionId) ?? throw new KeyNotFoundException("Pipeline definition was not found.");
        var run = new PipelineRun { DefinitionId = definition.Id, DefinitionName = definition.Name, Trigger = trigger, Ref = reference, ChangeId = changeId, CommitSha = commitSha };
        repository.AddRun(run); await runner.ExecuteAsync(definition, run, token); repository.UpdateRun(run); return run;
    }
}
