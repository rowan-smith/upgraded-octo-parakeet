namespace ForgeDeck.Build.Domain;

public enum PipelineRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    PartiallySucceeded
}

public enum JobStatus
{
    Queued,
    WaitingForRunner,
    Assigned,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped,
    Lost
}

public enum StepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped
}

public enum RunnerStatus
{
    Online,
    Busy,
    Offline
}

public enum TestOutcome
{
    Passed,
    Failed,
    Skipped
}

public enum FailureKind
{
    None,
    Infrastructure,
    Pipeline,
    Test,
    Cancellation
}
