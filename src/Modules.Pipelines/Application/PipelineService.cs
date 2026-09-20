using System.Text.Json;
using Modules.Pipelines.Domain;
using Platform.Contracts.Events;

namespace Modules.Pipelines.Application;

public sealed class PipelineService(IPipelineStore store, IEventPublisher events)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<PipelineDefinition> ListDefinitions(Guid? projectId = null) => store.ListDefinitions(projectId);
    public PipelineDefinition? FindDefinition(Guid id) => store.FindDefinition(id);
    public IReadOnlyList<PipelineRun> ListRuns(int take = 100) => store.ListRuns(take);
    public PipelineRun? FindRun(Guid id) => store.FindRun(id);
    public IReadOnlyList<PipelineRun> FindRunsForChange(Guid changeId) => store.FindRunsForChange(changeId);

    public PipelineDefinition AddDefinition(
        Guid projectId,
        string name,
        IReadOnlyList<PipelineTrigger> triggers,
        IReadOnlyList<PipelineJobDefinition> jobs,
        IReadOnlyDictionary<string, string>? environment = null,
        int timeoutSeconds = 3600)
    {
        if (string.IsNullOrWhiteSpace(name) || jobs.Count == 0)
            throw new ArgumentException("A pipeline needs a name and at least one job.");
        var definition = new PipelineDefinition
        {
            Name = name.Trim(),
            Triggers = triggers,
            Jobs = jobs,
            Environment = environment ?? new Dictionary<string, string>(),
            TimeoutSeconds = timeoutSeconds
        };
        store.SaveDefinition(projectId, definition);
        return definition;
    }

    public PipelineDefinition UpdateDefinition(Guid projectId, PipelineDefinition definition)
    {
        var existing = store.FindDefinition(definition.Id) ?? throw new KeyNotFoundException("Pipeline definition was not found.");
        definition.Version = existing.Version;
        definition.BumpVersion();
        store.SaveDefinition(projectId, definition);
        return definition;
    }

    public PipelineDefinition SetEnabled(Guid projectId, Guid definitionId, bool enabled)
    {
        var definition = store.FindDefinition(definitionId) ?? throw new KeyNotFoundException("Pipeline definition was not found.");
        definition.Enabled = enabled;
        definition.BumpVersion();
        store.SaveDefinition(projectId, definition);
        return definition;
    }

    public void DeleteDefinition(Guid definitionId)
    {
        if (store.FindDefinition(definitionId) is null)
            throw new KeyNotFoundException("Pipeline definition was not found.");
        store.DeleteDefinition(definitionId);
    }

    public PipelineRun StartRun(
        Guid definitionId,
        PipelineTrigger trigger,
        string reference,
        string commitSha,
        Guid? changeId = null,
        string? repositoryUrl = null)
    {
        var definition = store.FindDefinition(definitionId) ?? throw new KeyNotFoundException("Pipeline definition was not found.");
        if (!definition.Enabled)
            throw new InvalidOperationException("Pipeline definition is disabled.");
        if (string.IsNullOrWhiteSpace(commitSha) || commitSha.Equals("local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A real commit SHA is required (not 'local').", nameof(commitSha));

        var toSupersede = changeId is Guid change
            ? store.FindActiveRunsForChange(change, definition.Id).ToArray()
            : [];

        var snapshot = store.GetDefinitionVersionPayload(definition.Id, definition.Version)
                       ?? JsonSerializer.Serialize(definition, Json);
        var run = PipelineRun.Create(definition, snapshot, trigger, reference, commitSha, changeId, repositoryUrl);
        run.Start();
        store.SaveRun(run);

        foreach (var older in toSupersede)
        {
            older.Supersede(run.Id);
            store.SaveRun(older);
        }

        _ = events.PublishAsync(new PipelineRunStarted(run.Id, run.ChangeId, run.DefinitionName, run.CommitSha));
        return run;
    }

    public PipelineRun CancelRun(Guid runId, string? reason = null)
    {
        var run = store.FindRun(runId) ?? throw new KeyNotFoundException("Pipeline run was not found.");
        run.Cancel(reason);
        store.SaveRun(run);
        _ = events.PublishAsync(new PipelineRunCompleted(run.Id, run.ChangeId, run.DefinitionName, run.CommitSha, "Cancelled"));
        return run;
    }

    public PipelineRun RetryRun(Guid runId)
    {
        var previous = store.FindRun(runId) ?? throw new KeyNotFoundException("Pipeline run was not found.");
        return StartRun(
            previous.DefinitionId,
            PipelineTrigger.Manual,
            previous.Ref,
            previous.CommitSha,
            previous.ChangeId,
            previous.RepositoryUrl);
    }
}
