namespace ForgeDeck.Contracts.Services;

/// <summary>
/// Domain service contracts. In-process today; remote (HTTP/gRPC) later —
/// callers should depend on these interfaces, not sibling module Infrastructure.
/// </summary>
public interface IGitService
{
    string ServiceId { get; }
}

public interface ICodeService
{
    string ServiceId { get; }
}

public interface IReviewService
{
    string ServiceId { get; }
}

public interface IBuildService
{
    string ServiceId { get; }
}

public interface IDeployService
{
    string ServiceId { get; }
}
