using ForgeDeck.Build.Domain;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Pipelines;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Build.Application;

public sealed class JobExecutionService(IPipelineStore store, IOptions<PipelinesOptions> options, IEventPublisher events)
{
    private readonly PipelinesOptions _options = options.Value;

    public PipelineRun AppendLogs(Guid runId, Guid jobId, IReadOnlyList<LogChunkDto> chunks)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, jobId);
        foreach (var chunk in chunks)
        {
            var message = Truncate(chunk.Message);
            job.AppendLog(chunk.StepId, chunk.Stream, message, chunk.Timestamp);
        }
        EnforceLogBudget(job);
        store.SaveRun(run);
        return run;
    }

    public PipelineRun ReportStep(Guid runId, Guid jobId, StepReportDto report)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, jobId);
        if (!Enum.TryParse<StepStatus>(report.Status, true, out var status))
        {
            throw new ArgumentException($"Unknown step status '{report.Status}'.");
        }

        job.ReportStep(report.StepId, status, report.ExitCode, report.FailureReason, report.StartedAt, report.CompletedAt);
        store.SaveRun(run);
        return run;
    }

    public PipelineRun CompleteJob(Guid runId, JobCompleteDto report)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, report.JobId);
        if (!Enum.TryParse<JobStatus>(report.Status, true, out var status))
        {
            throw new ArgumentException($"Unknown job status '{report.Status}'.");
        }

        var failureKind = Enum.TryParse<FailureKind>(report.FailureKind, true, out var kind) ? kind : FailureKind.None;
        if (status == JobStatus.Failed && failureKind == FailureKind.None)
        {
            failureKind = FailureKind.Pipeline;
        }

        job.Complete(status, report.ExitCode, report.FailureReason, failureKind);

        if (job.RunnerId is Guid runnerId)
        {
            var runner = store.FindRunner(runnerId);
            if (runner is not null)
            {
                runner.CurrentJobCount = Math.Max(0, runner.CurrentJobCount - 1);
                var nextStatus = runner.CurrentJobCount > 0 ? RunnerStatus.Busy : RunnerStatus.Online;
                runner.Heartbeat(nextStatus, runner.Capabilities, runner.CurrentJobCount, runner.Version, runner.CpuCount, runner.MemoryBytes, runner.OperatingSystem);
                store.SaveRunner(runner);
            }
        }

        run.AdvanceAfterJobCompletion(job.Id);
        store.SaveRun(run);
        if (run.CompletedAt is not null)
        {
            _ = events.PublishAsync(new PipelineRunCompleted(run.Id, run.ChangeId, run.DefinitionName, run.CommitSha, run.Status.ToString()));
        }

        return run;
    }

    public PipelineRun AttachTestResults(Guid runId, Guid jobId, TestRun results)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, jobId);
        job.TestResults = results;
        store.SaveRun(run);
        return run;
    }

    public PipelineRun AttachArtifact(Guid runId, Guid jobId, RunArtifact artifact)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, jobId);
        job.Artifacts.Add(artifact);
        store.SaveRun(run);
        return run;
    }

    /// <summary>Simulated/deterministic completion used by SimulatedRunnerHostedService and unit tests.</summary>
    public PipelineRun SimulateJob(Guid runId, Guid jobId)
    {
        var run = RequireRun(runId);
        var job = RequireJob(run, jobId);
        if (job.Status is not (JobStatus.WaitingForRunner or JobStatus.Assigned or JobStatus.Queued))
        {
            return run;
        }

        if (job.Status == JobStatus.Queued)
        {
            job.MarkWaitingForRunner();
        }

        if (job.Status == JobStatus.WaitingForRunner)
        {
            job.Assign(Guid.Parse("00000000-0000-4000-8000-000000000099"));
        }

        job.MarkRunning();

        foreach (var step in job.Steps.OrderBy(s => s.Ordinal))
        {
            job.AppendLog(step.Id, "stdout", $"$ {step.Command}");
            step.ApplyReport(StepStatus.Running, null, null, DateTimeOffset.UtcNow, null);
            if (step.Command.Contains("exit 1", StringComparison.OrdinalIgnoreCase))
            {
                job.AppendLog(step.Id, "stderr", "Command failed with exit code 1");
                step.ApplyReport(StepStatus.Failed, 1, "Command failed with exit code 1", null, DateTimeOffset.UtcNow);
                job.Complete(JobStatus.Failed, 1, "Command failed with exit code 1", FailureKind.Pipeline);
                run.AdvanceAfterJobCompletion(job.Id);
                store.SaveRun(run);
                if (run.CompletedAt is not null)
                {
                    _ = events.PublishAsync(new PipelineRunCompleted(run.Id, run.ChangeId, run.DefinitionName, run.CommitSha, run.Status.ToString()));
                }

                return run;
            }

            job.AppendLog(step.Id, "stdout", "Completed successfully");
            step.ApplyReport(StepStatus.Succeeded, 0, null, null, DateTimeOffset.UtcNow);
        }

        job.Complete(JobStatus.Succeeded, 0, null, FailureKind.None);
        run.AdvanceAfterJobCompletion(job.Id);
        store.SaveRun(run);
        if (run.CompletedAt is not null)
        {
            _ = events.PublishAsync(new PipelineRunCompleted(run.Id, run.ChangeId, run.DefinitionName, run.CommitSha, run.Status.ToString()));
        }

        return run;
    }

    public async Task DrainSimulatedAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = RequireRun(runId);
            if (run.Status is PipelineRunStatus.Succeeded or PipelineRunStatus.Failed or PipelineRunStatus.Cancelled or PipelineRunStatus.PartiallySucceeded)
            {
                return;
            }

            var waiting = run.Jobs.OrderBy(j => j.Ordinal).FirstOrDefault(j => j.Status == JobStatus.WaitingForRunner);
            if (waiting is null)
            {
                return;
            }

            SimulateJob(runId, waiting.Id);
            await Task.Yield();
        }
    }

    private PipelineRun RequireRun(Guid runId) =>
        store.FindRun(runId) ?? throw new KeyNotFoundException("Pipeline run was not found.");

    private static PipelineJob RequireJob(PipelineRun run, Guid jobId) =>
        run.FindJob(jobId) ?? throw new KeyNotFoundException("Pipeline job was not found.");

    private string Truncate(string message)
    {
        if (message.Length <= _options.MaxLogLineLength)
        {
            return message;
        }

        return message[.._options.MaxLogLineLength] + "…";
    }

    private void EnforceLogBudget(PipelineJob job)
    {
        var bytes = job.Logs.Sum(l => l.Message.Length);
        while (bytes > _options.MaxLogBytesPerStep && job.Logs.Count > 1)
        {
            bytes -= job.Logs[0].Message.Length;
            job.Logs.RemoveAt(0);
        }
    }
}
