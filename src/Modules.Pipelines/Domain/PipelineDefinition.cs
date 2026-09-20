namespace Modules.Pipelines.Domain;

public sealed record PipelineStep(string Name, string Command);

public sealed class PipelineDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required IReadOnlyList<PipelineTrigger> Triggers { get; init; }
    public required IReadOnlyList<PipelineStep> Steps { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
