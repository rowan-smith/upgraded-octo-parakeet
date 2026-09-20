namespace Modules.Pipelines.Domain;

public enum PipelineRunStatus { Queued, Running, Passed, Failed }
public sealed record PipelineLogEntry(DateTimeOffset Timestamp, string Step, string Message);
public sealed record PipelineOutput(string Name, string Reference);

public sealed class PipelineRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DefinitionId { get; init; }
    public required string DefinitionName { get; init; }
    public required PipelineTrigger Trigger { get; init; }
    public required string Ref { get; init; }
    public Guid? ChangeId { get; init; }
    public string? CommitSha { get; init; }
    public PipelineRunStatus Status { get; private set; } = PipelineRunStatus.Queued;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public List<PipelineLogEntry> Logs { get; } = [];
    public List<PipelineOutput> Outputs { get; } = [];

    public void Start() { Status = PipelineRunStatus.Running; StartedAt = DateTimeOffset.UtcNow; }
    public void Log(string step, string message) => Logs.Add(new(DateTimeOffset.UtcNow, step, message));
    public void Complete(bool succeeded)
    {
        Status = succeeded ? PipelineRunStatus.Passed : PipelineRunStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        if (succeeded) Outputs.Add(new("build", $"run://{Id}/output/build"));
    }
}
