namespace ForgeDeck.Contracts.Checks;

public enum CheckStatus { Queued, Running, Passed, Failed, Cancelled, Skipped, Neutral }

public sealed record CheckResult(
    string Provider,
    string Name,
    CheckStatus Status,
    string Duration,
    string? DetailsUrl = null,
    string? Id = null,
    string? CommitSha = null,
    string? Summary = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null);

public interface ICheckProvider
{
    string Id { get; }
    Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default);
}

/// <summary>Evaluates whether named required checks succeed for a specific commit SHA.</summary>
public static class RequiredCheckEvaluator
{
    public static bool IsSatisfied(IReadOnlyList<CheckResult> checks, string commitSha, string requiredCheckName)
    {
        var exact = checks.Where(c => c.Name.Equals(requiredCheckName, StringComparison.OrdinalIgnoreCase)
            && c.CommitSha is not null
            && c.CommitSha.Equals(commitSha, StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => c.CompletedAt ?? c.StartedAt).FirstOrDefault();
        var candidate = exact ?? checks.Where(c => c.Name.Equals(requiredCheckName, StringComparison.OrdinalIgnoreCase)
            && (c.CommitSha is null || c.CommitSha.Equals(commitSha, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.CompletedAt ?? c.StartedAt).FirstOrDefault();
        return candidate is not null && candidate.Status == CheckStatus.Passed
            && (candidate.CommitSha is null || candidate.CommitSha.Equals(commitSha, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<(string Name, bool Satisfied, string Status)> EvaluateAll(
        IReadOnlyList<CheckResult> checks, string commitSha, IReadOnlyList<string> required)
        => required.Select(name =>
        {
            var exact = checks.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && c.CommitSha is not null && c.CommitSha.Equals(commitSha, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.CompletedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
            if (exact is null)
            {
                return (name, false, "Pending");
            }

            return (name, exact.Status == CheckStatus.Passed, exact.Status.ToString());
        }).ToArray();
}
