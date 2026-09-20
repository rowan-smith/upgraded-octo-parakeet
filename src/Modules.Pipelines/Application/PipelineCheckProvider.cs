using Modules.Pipelines.Domain;
using Platform.Contracts.Checks;

namespace Modules.Pipelines.Application;

public sealed class PipelineCheckProvider(IPipelineStore store) : ICheckProvider
{
    public string Id => "pipelines";

    public Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default)
    {
        var checks = store.FindRunsForChange(changeId)
            .SelectMany(run => run.Jobs
                .Where(job => job.PublishCheck)
                .Select(job => new { run, job }))
            .GroupBy(item => (CheckName: item.job.CheckName, CommitSha: item.run.CommitSha), StringTupleComparer.Instance)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.run.CreatedAt).First();
                return ToCheck(latest.run, latest.job);
            })
            .OrderBy(check => check.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<CheckResult>>(checks);
    }

    private static CheckResult ToCheck(PipelineRun run, PipelineJob job)
    {
        var status = Map(job.Status, run.Status);
        var summary = job.TestResults is { } tests
            ? $"{tests.Passed} passed, {tests.Failed} failed, {tests.Skipped} skipped"
            : job.FailureReason;
        return new CheckResult(
            Provider: "Pipelines",
            Name: job.CheckName,
            Status: status,
            Duration: Duration(job),
            DetailsUrl: $"#/runs/{run.Id}",
            Id: $"{run.Id:N}:{job.Id:N}",
            CommitSha: run.CommitSha,
            Summary: summary,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt);
    }

    private static CheckStatus Map(JobStatus jobStatus, PipelineRunStatus runStatus) => jobStatus switch
    {
        JobStatus.Queued or JobStatus.WaitingForRunner or JobStatus.Assigned => CheckStatus.Queued,
        JobStatus.Running => CheckStatus.Running,
        JobStatus.Succeeded => CheckStatus.Passed,
        JobStatus.Cancelled => CheckStatus.Cancelled,
        JobStatus.Skipped => CheckStatus.Skipped,
        JobStatus.Failed or JobStatus.Lost => CheckStatus.Failed,
        _ when runStatus == PipelineRunStatus.Cancelled => CheckStatus.Cancelled,
        _ => CheckStatus.Neutral
    };

    private static string Duration(PipelineJob job)
    {
        if (job.StartedAt is null) return "—";
        return ((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.StartedAt.Value).TotalSeconds.ToString("0.0s");
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string CheckName, string CommitSha)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string CheckName, string CommitSha) x, (string CheckName, string CommitSha) y) =>
            string.Equals(x.CheckName, y.CheckName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.CommitSha, y.CommitSha, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string CheckName, string CommitSha) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CheckName), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.CommitSha));
    }
}
