using ForgeDeck.Contracts.Pipelines;
using Microsoft.Extensions.Logging;

namespace ForgeDeck.Runner;

internal sealed class JobWorker(
    RunnerClient client,
    WorkspaceManager workspaces,
    ProcessExecutor processes,
    ILogger<JobWorker> logger)
{
    public async Task ExecuteAsync(RunnerCredentials credentials, JobAssignmentDto job, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting job {JobName} ({JobId}) for run {RunId}", job.JobName, job.JobId, job.RunId);
        var sourceDir = workspaces.CreateWorkspace(job.WorkingDirectoryHint);
        var failed = false;
        string? failureReason = null;
        int? exitCode = null;

        try
        {
            await workspaces.PrepareSourceAsync(sourceDir, job.RepositoryUrl, job.CloneToken, job.CommitSha, cancellationToken);

            var env = new Dictionary<string, string>(job.Environment, StringComparer.OrdinalIgnoreCase);
            foreach (var step in job.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = DateTimeOffset.UtcNow;
                await client.ReportStepAsync(credentials, job.RunId, job.JobId,
                    new StepReportDto(step.StepId, "Running", null, null, started, null), cancellationToken);

                var stepEnv = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in step.Environment)
                {
                    stepEnv[pair.Key] = pair.Value;
                }

                var logBuffer = new List<LogChunkDto>();
                async Task FlushLogs()
                {
                    if (logBuffer.Count == 0)
                    {
                        return;
                    }

                    var batch = logBuffer.ToArray();
                    logBuffer.Clear();
                    await client.AppendLogsAsync(credentials, job.RunId, job.JobId, batch, cancellationToken);
                }

                await client.AppendLogsAsync(credentials, job.RunId, job.JobId,
                [
                    new LogChunkDto(job.JobId, step.StepId, DateTimeOffset.UtcNow, "stdout", $"$ {step.Command}")
                ], cancellationToken);

                ProcessResult result;
                try
                {
                    result = await processes.RunAsync(
                        step.Command,
                        sourceDir,
                        job.Shell,
                        stepEnv,
                        step.TimeoutSeconds > 0 ? step.TimeoutSeconds : job.TimeoutSeconds,
                        async (stream, line) =>
                        {
                            logBuffer.Add(new LogChunkDto(job.JobId, step.StepId, DateTimeOffset.UtcNow, stream, line));
                            if (logBuffer.Count >= 20)
                            {
                                await FlushLogs();
                            }
                        },
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await FlushLogs();
                    await client.ReportStepAsync(credentials, job.RunId, job.JobId,
                        new StepReportDto(step.StepId, "Cancelled", null, "Cancelled", started, DateTimeOffset.UtcNow), cancellationToken);
                    await client.CompleteJobAsync(credentials, job.RunId,
                        new JobCompleteDto(job.JobId, "Cancelled", null, "Cancelled", "Cancellation"), cancellationToken);
                    failed = true;
                    return;
                }

                await FlushLogs();
                var succeeded = result.ExitCode == 0;
                await client.ReportStepAsync(credentials, job.RunId, job.JobId,
                    new StepReportDto(
                        step.StepId,
                        succeeded ? "Succeeded" : "Failed",
                        result.ExitCode,
                        succeeded ? null : $"Exit code {result.ExitCode}",
                        started,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                if (!succeeded)
                {
                    failed = true;
                    exitCode = result.ExitCode;
                    failureReason = $"Step '{step.Name}' failed with exit code {result.ExitCode}";
                    break;
                }
            }

            await UploadArtifactsAndTestsAsync(credentials, job, sourceDir, cancellationToken);

            await client.CompleteJobAsync(credentials, job.RunId, new JobCompleteDto(
                job.JobId,
                failed ? "Failed" : "Succeeded",
                exitCode ?? (failed ? 1 : 0),
                failureReason,
                failed ? "Pipeline" : null), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failed = true;
            logger.LogError(exception, "Job {JobId} failed", job.JobId);
            await client.AppendLogsAsync(credentials, job.RunId, job.JobId,
            [
                new LogChunkDto(job.JobId, null, DateTimeOffset.UtcNow, "stderr", exception.Message)
            ], CancellationToken.None);
            await client.CompleteJobAsync(credentials, job.RunId,
                new JobCompleteDto(job.JobId, "Failed", 1, exception.Message, "Infrastructure"), CancellationToken.None);
        }
        finally
        {
            workspaces.Cleanup(sourceDir, failed);
        }
    }

    private async Task UploadArtifactsAndTestsAsync(
        RunnerCredentials credentials, JobAssignmentDto job, string sourceDir, CancellationToken cancellationToken)
    {
        var files = WorkspaceManager.MatchGlobs(sourceDir, job.ArtifactGlobs);
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            var name = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            var upload = new ArtifactUploadDto(
                job.JobId,
                name,
                GuessContentType(file),
                bytes.LongLength,
                Convert.ToBase64String(bytes));
            var uploaded = await client.UploadArtifactAsync(credentials, job.RunId, job.JobId, upload, cancellationToken);
            if (!uploaded)
            {
                logger.LogInformation("Artifact endpoint unavailable; skipping binary upload for {Name} ({Size} bytes)", name, bytes.LongLength);
            }
        }

        var trxFiles = Directory.Exists(sourceDir)
            ? Directory.EnumerateFiles(sourceDir, "*.trx", SearchOption.AllDirectories).ToArray()
            : [];
        foreach (var trx in trxFiles)
        {
            var content = await File.ReadAllTextAsync(trx, cancellationToken);
            await client.UploadTestsAsync(credentials, job.RunId, job.JobId, new TestResultUploadDto(job.JobId, "trx", content), cancellationToken);
            logger.LogInformation("Uploaded TRX results from {File}", trx);
        }
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".trx" or ".xml" => "application/xml",
        ".json" => "application/json",
        ".txt" or ".log" => "text/plain",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };
}
