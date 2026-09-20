namespace Platform.Contracts.Checks;

public enum CheckStatus { Queued, Running, Passed, Failed }

public sealed record CheckResult(
    string Provider,
    string Name,
    CheckStatus Status,
    string Duration,
    string? DetailsUrl = null);

public interface ICheckProvider
{
    string Id { get; }
    Task<IReadOnlyList<CheckResult>> GetChecksAsync(Guid changeId, CancellationToken cancellationToken = default);
}
