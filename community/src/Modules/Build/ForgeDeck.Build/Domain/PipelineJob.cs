using System.Text.Json.Serialization;

namespace ForgeDeck.Build.Domain;

public sealed class PipelineJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public int Ordinal { get; init; }
    [JsonInclude] public JobStatus Status { get; private set; } = JobStatus.Queued;
    public bool PublishCheck { get; init; }
    public string CheckName { get; init; } = string.Empty;
    public bool ContinueOnError { get; init; }
    public IReadOnlyList<string> RequiresCapabilities { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> ArtifactGlobs { get; init; } = [];
    public int TimeoutSeconds { get; init; } = 3600;
    public string Shell { get; init; } = "";
    public List<PipelineStep> Steps { get; init; } = [];
    [JsonInclude] public Guid? AssignmentId { get; private set; }
    [JsonInclude] public Guid? RunnerId { get; private set; }
    [JsonInclude] public int? ExitCode { get; private set; }
    [JsonInclude] public string? FailureReason { get; private set; }
    [JsonInclude] public FailureKind FailureKind { get; private set; } = FailureKind.None;
    [JsonInclude] public DateTimeOffset? QueuedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? WaitingAt { get; private set; }
    [JsonInclude] public DateTimeOffset? AssignedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? StartedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? CompletedAt { get; private set; }
    [JsonInclude] public bool CancelAcknowledged { get; private set; }
    public List<PipelineLogEntry> Logs { get; init; } = [];
    public List<RunArtifact> Artifacts { get; init; } = [];
    public TestRun? TestResults { get; set; }

    public static PipelineJob FromDefinition(PipelineJobDefinition definition, int ordinal) => new()
    {
        Name = definition.Name,
        Ordinal = ordinal,
        PublishCheck = definition.PublishCheck,
        CheckName = definition.EffectiveCheckName,
        ContinueOnError = definition.ContinueOnError,
        RequiresCapabilities = definition.RequiresCapabilities,
        Environment = definition.Environment,
        ArtifactGlobs = definition.ArtifactGlobs,
        TimeoutSeconds = definition.TimeoutSeconds,
        Shell = definition.Steps.FirstOrDefault()?.Shell ?? "",
        Steps = definition.Steps.Select((step, index) => PipelineStep.FromDefinition(step, index)).ToList()
    };

    public void MarkQueued()
    {
        Status = JobStatus.Queued;
        QueuedAt ??= DateTimeOffset.UtcNow;
    }

    public void MarkWaitingForRunner()
    {
        Ensure(JobStatus.Queued, JobStatus.WaitingForRunner);
        Status = JobStatus.WaitingForRunner;
        WaitingAt = DateTimeOffset.UtcNow;
    }

    public Guid Assign(Guid runnerId)
    {
        Ensure(JobStatus.WaitingForRunner);
        Status = JobStatus.Assigned;
        RunnerId = runnerId;
        AssignmentId = Guid.NewGuid();
        AssignedAt = DateTimeOffset.UtcNow;
        return AssignmentId.Value;
    }

    public void MarkRunning()
    {
        Ensure(JobStatus.Assigned, JobStatus.Running);
        Status = JobStatus.Running;
        StartedAt ??= DateTimeOffset.UtcNow;
    }

    public void AppendLog(Guid? stepId, string stream, string message, DateTimeOffset? timestamp = null)
    {
        Logs.Add(new PipelineLogEntry(timestamp ?? DateTimeOffset.UtcNow, stepId, stream, message));
    }

    public void ReportStep(Guid stepId, StepStatus status, int? exitCode, string? failureReason, DateTimeOffset? startedAt, DateTimeOffset? completedAt)
    {
        var step = Steps.FirstOrDefault(s => s.Id == stepId) ?? throw new KeyNotFoundException($"Step {stepId} was not found.");
        step.ApplyReport(status, exitCode, failureReason, startedAt, completedAt);
        if (status == StepStatus.Running && Status == JobStatus.Assigned)
        {
            MarkRunning();
        }
    }

    public void Complete(JobStatus status, int? exitCode, string? failureReason, FailureKind failureKind)
    {
        if (status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Lost or JobStatus.Skipped))
        {
            throw new InvalidOperationException($"Cannot complete job with status {status}.");
        }

        Status = status;
        ExitCode = exitCode;
        FailureReason = failureReason;
        FailureKind = failureKind;
        CompletedAt = DateTimeOffset.UtcNow;
        StartedAt ??= CompletedAt;
    }

    public void Cancel(string? reason = null)
    {
        if (Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped or JobStatus.Lost)
        {
            return;
        }

        foreach (var step in Steps.Where(s => s.Status is StepStatus.Pending or StepStatus.Running))
        {
            step.ApplyReport(StepStatus.Cancelled, null, reason ?? "Cancelled", null, DateTimeOffset.UtcNow);
        }

        Complete(JobStatus.Cancelled, null, reason ?? "Cancelled", FailureKind.Cancellation);
    }

    public void Skip(string reason)
    {
        if (Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped or JobStatus.Lost)
        {
            return;
        }

        foreach (var step in Steps.Where(s => s.Status == StepStatus.Pending))
        {
            step.ApplyReport(StepStatus.Skipped, null, reason, null, DateTimeOffset.UtcNow);
        }

        Complete(JobStatus.Skipped, null, reason, FailureKind.None);
    }

    public void MarkLost(string reason)
    {
        Complete(JobStatus.Lost, null, reason, FailureKind.Infrastructure);
    }

    public void AcknowledgeCancel()
    {
        if (Status != JobStatus.Cancelled)
        {
            return;
        }

        CancelAcknowledged = true;
    }

    public bool IsPendingCancelNotification(Guid runnerId, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        return Status == JobStatus.Cancelled
               && RunnerId == runnerId
               && !CancelAcknowledged
               && AssignedAt is not null
               && CompletedAt is not null
               && clock - CompletedAt.Value <= TimeSpan.FromMinutes(10);
    }

    private void Ensure(params JobStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOperationException($"Job '{Name}' is {Status}; expected one of: {string.Join(", ", allowed)}.");
        }
    }
}

public sealed class PipelineStep
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string? Shell { get; init; }
    public int Ordinal { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public int TimeoutSeconds { get; init; } = 600;
    public bool ContinueOnError { get; init; }
    [JsonInclude] public StepStatus Status { get; private set; } = StepStatus.Pending;
    [JsonInclude] public int? ExitCode { get; private set; }
    [JsonInclude] public string? FailureReason { get; private set; }
    [JsonInclude] public DateTimeOffset? StartedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? CompletedAt { get; private set; }

    public static PipelineStep FromDefinition(PipelineStepDefinition definition, int ordinal) => new()
    {
        Name = definition.Name,
        Command = definition.Command,
        Shell = definition.Shell,
        Ordinal = ordinal,
        Environment = definition.Environment,
        TimeoutSeconds = definition.TimeoutSeconds,
        ContinueOnError = definition.ContinueOnError
    };

    public void ApplyReport(StepStatus status, int? exitCode, string? failureReason, DateTimeOffset? startedAt, DateTimeOffset? completedAt)
    {
        Status = status;
        ExitCode = exitCode;
        FailureReason = failureReason;
        if (startedAt.HasValue)
        {
            StartedAt = startedAt;
        }
        else if (status == StepStatus.Running)
        {
            StartedAt ??= DateTimeOffset.UtcNow;
        }

        if (completedAt.HasValue)
        {
            CompletedAt = completedAt;
        }
        else if (status is StepStatus.Succeeded or StepStatus.Failed or StepStatus.Cancelled or StepStatus.Skipped)
        {
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }
}

public sealed record PipelineLogEntry(DateTimeOffset Timestamp, Guid? StepId, string Stream, string Message);
