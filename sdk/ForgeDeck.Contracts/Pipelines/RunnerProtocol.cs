namespace ForgeDeck.Contracts.Pipelines;

public sealed record RunnerRegistrationRequest(string Name, string RegistrationToken, string? OperatingSystem, IReadOnlyList<string> Capabilities, int Concurrency, string Version);
public sealed record RunnerRegistrationResponse(Guid RunnerId, string RunnerToken, string Warning);
public sealed record RunnerHeartbeatRequest(
    string Status,
    IReadOnlyList<string> Capabilities,
    int CurrentJobCount,
    string Version,
    int? CpuCount = null,
    long? MemoryBytes = null,
    string? OperatingSystem = null,
    IReadOnlyList<Guid>? AcknowledgedCancelJobIds = null);
public sealed record RunnerHeartbeatResponse(IReadOnlyList<Guid> CancelJobIds);
public sealed record RunnerWorkRequest(int MaxJobs = 1);
public sealed record JobAssignmentDto(
    Guid AssignmentId,
    Guid RunId,
    Guid JobId,
    string JobName,
    string CommitSha,
    string RepositoryUrl,
    string? CloneToken,
    string WorkingDirectoryHint,
    IReadOnlyList<StepAssignmentDto> Steps,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> ArtifactGlobs,
    IReadOnlyList<string> RequiredCapabilities,
    int TimeoutSeconds,
    string Shell);
public sealed record StepAssignmentDto(Guid StepId, string Name, string Command, int TimeoutSeconds, IReadOnlyDictionary<string, string> Environment);
public sealed record LogChunkDto(Guid JobId, Guid? StepId, DateTimeOffset Timestamp, string Stream, string Message);
public sealed record StepReportDto(Guid StepId, string Status, int? ExitCode, string? FailureReason, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
public sealed record JobCompleteDto(Guid JobId, string Status, int? ExitCode, string? FailureReason, string? FailureKind);
public sealed record TestResultUploadDto(Guid JobId, string Format, string Content);
public sealed record ArtifactUploadMetaDto(Guid JobId, string Name, string ContentType, long Size);
public sealed record ArtifactUploadDto(Guid JobId, string Name, string ContentType, long Size, string ContentBase64);
