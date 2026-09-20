using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Api;

public sealed record CreatePipelineRequest(string Name, IReadOnlyList<PipelineTrigger> Triggers, IReadOnlyList<PipelineStep> Steps, Dictionary<string, string>? Environment);
public sealed record RunPipelineRequest(Guid DefinitionId, string Ref = "main");
