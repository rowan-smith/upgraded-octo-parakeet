using System.Security.Cryptography;
using Modules.Pipelines.Domain;
using Modules.Pipelines.Infrastructure;
using Platform.Contracts.Integrations;
using Platform.Contracts.Pipelines;
using Platform.Core.Context;

namespace Modules.Pipelines.Application;

public sealed class RunnerService(
    IPipelineStore store,
    IProviderCredentialStore credentials,
    PlatformContextStore platform)
{
    public (string Token, RunnerRegistrationToken Record) CreateRegistrationToken(string createdBy, TimeSpan? lifetime = null)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var record = new RunnerRegistrationToken
        {
            TokenHash = SqlitePipelineStore.HashToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromHours(24)),
            CreatedBy = createdBy
        };
        store.SaveRegistrationToken(record);
        return (raw, record);
    }

    public RunnerRegistrationResponse Register(RunnerRegistrationRequest request)
    {
        var hash = SqlitePipelineStore.HashToken(request.RegistrationToken);
        var token = store.FindRegistrationTokenByHash(hash)
                    ?? throw new UnauthorizedAccessException("Registration token is invalid.");
        if (token.IsConsumed)
            throw new InvalidOperationException("Registration token has already been used.");
        if (token.IsExpired(DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Registration token has expired.");

        var runnerToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var runner = new RunnerAgent
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? "runner" : request.Name.Trim(),
            TokenHash = SqlitePipelineStore.HashToken(runnerToken),
            OperatingSystem = request.OperatingSystem,
            Capabilities = request.Capabilities,
            Concurrency = Math.Max(1, request.Concurrency),
            Version = request.Version
        };
        runner.Heartbeat(RunnerStatus.Online, request.Capabilities, 0, request.Version, null, null, request.OperatingSystem);
        store.SaveRunner(runner);
        store.ConsumeRegistrationToken(token.Id);

        return new RunnerRegistrationResponse(
            runner.Id,
            runnerToken,
            "WARNING: Pipeline commands run with this runner's OS permissions and can execute arbitrary repository-controlled code. Store the runner token securely; it cannot be retrieved again.");
    }

    public RunnerHeartbeatResponse Heartbeat(Guid runnerId, string runnerToken, RunnerHeartbeatRequest request)
    {
        var runner = Authenticate(runnerId, runnerToken);
        var status = Enum.TryParse<RunnerStatus>(request.Status, true, out var parsed) ? parsed : RunnerStatus.Online;
        runner.Heartbeat(status, request.Capabilities, request.CurrentJobCount, request.Version, request.CpuCount, request.MemoryBytes, request.OperatingSystem);
        store.SaveRunner(runner);

        if (request.AcknowledgedCancelJobIds is { Count: > 0 } acked)
        {
            var acknowledged = acked.ToHashSet();
            foreach (var run in store.ListRuns(100))
            {
                var dirty = false;
                foreach (var job in run.Jobs.Where(j => acknowledged.Contains(j.Id)))
                {
                    job.AcknowledgeCancel();
                    dirty = true;
                }
                if (dirty) store.SaveRun(run);
            }
        }

        return new RunnerHeartbeatResponse(FindCancelledJobsForRunner(runnerId));
    }

    public IReadOnlyList<JobAssignmentDto> RequestWork(Guid runnerId, string runnerToken, RunnerWorkRequest request)
    {
        var runner = Authenticate(runnerId, runnerToken);
        var maxJobs = Math.Max(1, request.MaxJobs);
        var availableSlots = Math.Max(0, runner.Concurrency - runner.CurrentJobCount);
        if (availableSlots == 0) return [];

        var cloneToken = credentials.Get("github");
        var assignments = new List<JobAssignmentDto>();
        foreach (var run in store.ListRunsWithJobsWaitingForRunner())
        {
            if (assignments.Count >= maxJobs || assignments.Count >= availableSlots)
                break;

            var job = run.Jobs.OrderBy(j => j.Ordinal).FirstOrDefault(j => j.Status == JobStatus.WaitingForRunner);
            if (job is null || !runner.Supports(job.RequiresCapabilities))
                continue;

            var assignmentId = job.Assign(runner.Id);
            store.SaveRun(run);
            runner.CurrentJobCount += 1;
            if (runner.CurrentJobCount >= runner.Concurrency)
                runner.Heartbeat(RunnerStatus.Busy, runner.Capabilities, runner.CurrentJobCount, runner.Version, runner.CpuCount, runner.MemoryBytes, runner.OperatingSystem);
            store.SaveRunner(runner);

            assignments.Add(ToAssignment(run, job, assignmentId, cloneToken));
        }

        return assignments;
    }

    public IReadOnlyList<RunnerAgent> ListRunners() => store.ListRunners();

    public void Revoke(Guid runnerId)
    {
        if (store.FindRunner(runnerId) is null)
            throw new KeyNotFoundException("Runner was not found.");
        store.RevokeRunner(runnerId);
    }

    public RunnerAgent Authenticate(Guid runnerId, string runnerToken)
    {
        var runner = store.FindRunner(runnerId) ?? throw new UnauthorizedAccessException("Runner was not found.");
        var hash = SqlitePipelineStore.HashToken(runnerToken);
        if (!string.Equals(runner.TokenHash, hash, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Runner token is invalid.");
        return runner;
    }

    private IReadOnlyList<Guid> FindCancelledJobsForRunner(Guid runnerId) =>
        store.ListRuns(100)
            .SelectMany(run => run.Jobs
                .Where(job => job.IsPendingCancelNotification(runnerId))
                .Select(job => job.Id))
            .Distinct()
            .ToArray();

    private JobAssignmentDto ToAssignment(PipelineRun run, PipelineJob job, Guid assignmentId, string? cloneToken) =>
        new(
            assignmentId,
            run.Id,
            job.Id,
            job.Name,
            run.CommitSha,
            run.RepositoryUrl ?? string.Empty,
            cloneToken,
            $"run-{run.Id:N}/job-{job.Id:N}",
            job.Steps.OrderBy(s => s.Ordinal).Select(s => new StepAssignmentDto(
                s.Id, s.Name, s.Command, s.TimeoutSeconds, s.Environment)).ToArray(),
            MergeEnvironment(run, job),
            job.ArtifactGlobs,
            job.RequiresCapabilities,
            job.TimeoutSeconds,
            job.Shell);

    private IReadOnlyDictionary<string, string> MergeEnvironment(PipelineRun run, PipelineJob job)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLATFORM_PROJECT_ID"] = platform.Project.Id.ToString(),
            ["PIPELINE_ID"] = run.DefinitionId.ToString(),
            ["PIPELINE_RUN_ID"] = run.Id.ToString(),
            ["COMMIT_SHA"] = run.CommitSha,
            ["SOURCE_BRANCH"] = run.Ref,
            ["FORGEDECK_RUN_ID"] = run.Id.ToString(),
            ["FORGEDECK_JOB_ID"] = job.Id.ToString(),
            ["FORGEDECK_COMMIT_SHA"] = run.CommitSha,
            ["FORGEDECK_REF"] = run.Ref
        };
        if (run.ChangeId is Guid changeId)
            merged["CHANGE_ID"] = changeId.ToString();
        foreach (var pair in job.Environment)
            merged[pair.Key] = pair.Value;
        return merged;
    }
}
