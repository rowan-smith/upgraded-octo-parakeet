using System.Text.Json.Serialization;

namespace ForgeDeck.Build.Domain;

public sealed class PipelineRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid DefinitionId { get; init; }
    public required string DefinitionName { get; init; }
    public required int DefinitionVersion { get; init; }
    public required string DefinitionSnapshotJson { get; init; }
    public required PipelineTrigger Trigger { get; init; }
    public required string Ref { get; init; }
    public required string CommitSha { get; init; }
    public Guid? ChangeId { get; init; }
    public string? RepositoryUrl { get; init; }
    [JsonInclude] public Guid? SupersededByRunId { get; private set; }
    [JsonInclude] public bool IsSuperseded { get; private set; }
    [JsonInclude] public PipelineRunStatus Status { get; private set; } = PipelineRunStatus.Queued;
    [JsonInclude] public FailureKind FailureKind { get; private set; } = FailureKind.None;
    [JsonInclude] public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonInclude] public DateTimeOffset? StartedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? CompletedAt { get; private set; }
    public List<PipelineJob> Jobs { get; init; } = [];

    public static PipelineRun Create(
        PipelineDefinition definition,
        string definitionSnapshotJson,
        PipelineTrigger trigger,
        string reference,
        string commitSha,
        Guid? changeId = null,
        string? repositoryUrl = null)
    {
        var run = new PipelineRun
        {
            DefinitionId = definition.Id,
            DefinitionName = definition.Name,
            DefinitionVersion = definition.Version,
            DefinitionSnapshotJson = definitionSnapshotJson,
            Trigger = trigger,
            Ref = reference,
            CommitSha = commitSha,
            ChangeId = changeId,
            RepositoryUrl = repositoryUrl,
            Jobs = definition.Jobs.Select((job, index) => PipelineJob.FromDefinition(job, index)).ToList()
        };
        foreach (var job in run.Jobs)
        {
            job.MarkQueued();
        }

        return run;
    }

    public PipelineJob? FindJob(Guid jobId) => Jobs.FirstOrDefault(j => j.Id == jobId);

    public void Start()
    {
        if (Status is PipelineRunStatus.Cancelled or PipelineRunStatus.Succeeded or PipelineRunStatus.Failed or PipelineRunStatus.PartiallySucceeded)
        {
            throw new InvalidOperationException($"Cannot start a run in status {Status}.");
        }

        Status = PipelineRunStatus.Running;
        StartedAt ??= DateTimeOffset.UtcNow;
        var first = Jobs.OrderBy(j => j.Ordinal).FirstOrDefault(j => j.Status == JobStatus.Queued);
        first?.MarkWaitingForRunner();
    }

    public void Cancel(string? reason = null)
    {
        if (Status is PipelineRunStatus.Succeeded or PipelineRunStatus.Failed or PipelineRunStatus.Cancelled or PipelineRunStatus.PartiallySucceeded)
        {
            return;
        }

        foreach (var job in Jobs.Where(j => j.Status is not (JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped or JobStatus.Lost)))
        {
            job.Cancel(reason);
        }

        Status = PipelineRunStatus.Cancelled;
        FailureKind = FailureKind.Cancellation;
        FailureReason = reason ?? "Cancelled";
        CompletedAt = DateTimeOffset.UtcNow;
        StartedAt ??= CompletedAt;
    }

    public void Supersede(Guid newerRunId)
    {
        IsSuperseded = true;
        SupersededByRunId = newerRunId;
        if (Status is PipelineRunStatus.Queued or PipelineRunStatus.Running)
        {
            Cancel("Superseded by a newer run for the same change.");
        }
    }

    public void AdvanceAfterJobCompletion(Guid jobId)
    {
        var job = FindJob(jobId) ?? throw new KeyNotFoundException($"Job {jobId} was not found.");
        if (job.Status is JobStatus.Failed or JobStatus.Lost)
        {
            if (!job.ContinueOnError)
            {
                foreach (var remaining in Jobs.Where(j => j.Ordinal > job.Ordinal && j.Status is JobStatus.Queued or JobStatus.WaitingForRunner))
                {
                    remaining.Skip($"Skipped because '{job.Name}' failed.");
                }

                FinalizeFromJobs();
                return;
            }
        }

        if (Status == PipelineRunStatus.Cancelled)
        {
            return;
        }

        var next = Jobs.OrderBy(j => j.Ordinal).FirstOrDefault(j => j.Status == JobStatus.Queued);
        if (next is not null)
        {
            Status = PipelineRunStatus.Running;
            StartedAt ??= DateTimeOffset.UtcNow;
            next.MarkWaitingForRunner();
            return;
        }

        FinalizeFromJobs();
    }

    public void FinalizeFromJobs()
    {
        if (Jobs.Any(j => j.Status is JobStatus.Queued or JobStatus.WaitingForRunner or JobStatus.Assigned or JobStatus.Running))
        {
            return;
        }

        StartedAt ??= DateTimeOffset.UtcNow;
        CompletedAt = DateTimeOffset.UtcNow;

        if (Jobs.Any(j => j.Status == JobStatus.Cancelled) && Jobs.All(j => j.Status is JobStatus.Cancelled or JobStatus.Skipped or JobStatus.Succeeded))
        {
            Status = PipelineRunStatus.Cancelled;
            FailureKind = FailureKind.Cancellation;
            return;
        }

        var hardFailures = Jobs.Where(j => j.Status is JobStatus.Failed or JobStatus.Lost && !j.ContinueOnError).ToArray();
        var softFailures = Jobs.Where(j => j.Status is JobStatus.Failed or JobStatus.Lost && j.ContinueOnError).ToArray();
        if (hardFailures.Length > 0)
        {
            Status = PipelineRunStatus.Failed;
            var first = hardFailures[0];
            FailureKind = first.FailureKind == FailureKind.None ? FailureKind.Pipeline : first.FailureKind;
            FailureReason = first.FailureReason;
            return;
        }

        if (softFailures.Length > 0 && Jobs.Any(j => j.Status == JobStatus.Succeeded))
        {
            Status = PipelineRunStatus.PartiallySucceeded;
            FailureKind = softFailures[0].FailureKind == FailureKind.None ? FailureKind.Pipeline : softFailures[0].FailureKind;
            FailureReason = softFailures[0].FailureReason;
            return;
        }

        if (softFailures.Length > 0)
        {
            Status = PipelineRunStatus.Failed;
            FailureKind = softFailures[0].FailureKind == FailureKind.None ? FailureKind.Pipeline : softFailures[0].FailureKind;
            FailureReason = softFailures[0].FailureReason;
            return;
        }

        Status = PipelineRunStatus.Succeeded;
        FailureKind = FailureKind.None;
        FailureReason = null;
    }
}
