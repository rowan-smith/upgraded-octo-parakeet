using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Infrastructure;

public sealed class InMemoryPipelineRepository : IPipelineRepository
{
    public static readonly Guid BuildDefinitionId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private readonly List<PipelineDefinition> _definitions = [SeedDefinition()];
    private readonly List<PipelineRun> _runs = [SeedRun()];
    private readonly object _gate = new();
    public IReadOnlyList<PipelineDefinition> ListDefinitions() { lock (_gate) return _definitions.ToArray(); }
    public PipelineDefinition? FindDefinition(Guid id) { lock (_gate) return _definitions.FirstOrDefault(definition => definition.Id == id); }
    public void AddDefinition(PipelineDefinition definition) { lock (_gate) _definitions.Add(definition); }
    public IReadOnlyList<PipelineRun> ListRuns() { lock (_gate) return _runs.OrderByDescending(run => run.CreatedAt).ToArray(); }
    public PipelineRun? FindRun(Guid id) { lock (_gate) return _runs.FirstOrDefault(run => run.Id == id); }
    public IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId) { lock (_gate) return _runs.Where(run => run.ChangeId == changeId).OrderByDescending(run => run.CreatedAt).ToArray(); }
    public void AddRun(PipelineRun run) { lock (_gate) _runs.Add(run); }
    public void UpdateRun(PipelineRun run) { lock (_gate) { } }

    private static PipelineDefinition SeedDefinition() => new()
    {
        Id = BuildDefinitionId, Name = "Build and test", Triggers = [PipelineTrigger.Manual, PipelineTrigger.Push, PipelineTrigger.ChangeOpened],
        Steps = [new("Restore", "dotnet restore"), new("Build", "dotnet build --no-restore"), new("Unit tests", "dotnet test --no-build")],
        Environment = new Dictionary<string, string> { ["DOTNET_NOLOGO"] = "true" }
    };
    private static PipelineRun SeedRun()
    {
        var run = new PipelineRun { Id = Guid.Parse("20000000-0000-4000-8000-000000000912"), DefinitionId = BuildDefinitionId,
            DefinitionName = "Build and test", Trigger = PipelineTrigger.ChangeOpened, Ref = "feat/webhook-retries", ChangeId = Guid.Parse("deadd00d-0000-4000-8000-000000000142"), CommitSha = "f73a81a" };
        run.Start(); run.Log("Restore", "Dependencies restored in 1.2s"); run.Log("Build", "Build succeeded with 0 warnings"); run.Log("Unit tests", "42 tests passed"); run.Complete(true); return run;
    }
}
