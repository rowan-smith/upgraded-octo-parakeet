namespace Modules.Pipelines.Domain;

public sealed class PipelineDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public int Version { get; set; } = 1;
    public required IReadOnlyList<PipelineTrigger> Triggers { get; set; }
    public required IReadOnlyList<PipelineJobDefinition> Jobs { get; set; }
    public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    public int TimeoutSeconds { get; set; } = 3600;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void BumpVersion()
    {
        Version += 1;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed record PipelineJobDefinition(
    string Name,
    IReadOnlyList<PipelineStepDefinition> Steps,
    IReadOnlyList<string> RequiresCapabilities,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds,
    bool PublishCheck,
    string? CheckName,
    IReadOnlyList<string> ArtifactGlobs,
    bool ContinueOnError = false)
{
    public string EffectiveCheckName => string.IsNullOrWhiteSpace(CheckName) ? Name : CheckName;
}

public sealed record PipelineStepDefinition(
    string Name,
    string Command,
    string? Shell,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds,
    bool ContinueOnError = false);
