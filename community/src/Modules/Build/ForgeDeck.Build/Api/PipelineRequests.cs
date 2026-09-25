using ForgeDeck.Build.Domain;

namespace ForgeDeck.Build.Api;

public sealed record CreatePipelineRequest(
    string Name,
    IReadOnlyList<PipelineTrigger> Triggers,
    IReadOnlyList<PipelineJobDefinition> Jobs,
    Dictionary<string, string>? Environment = null,
    int TimeoutSeconds = 3600);

public sealed record RunPipelineRequest(Guid DefinitionId, string Ref = "main", string CommitSha = "", string? RepositoryUrl = null);

public sealed record CancelPipelineRequest(string? Reason = null);

public sealed record CreateRegistrationTokenRequest(int LifetimeHours = 24);
