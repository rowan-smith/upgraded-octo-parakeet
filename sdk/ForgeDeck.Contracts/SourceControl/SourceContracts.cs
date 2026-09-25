namespace ForgeDeck.Contracts.SourceControl;

public sealed record RepositoryId(string Provider, string Owner, string Name)
{
    public override string ToString() => $"{Provider}:{Owner}/{Name}";
}

public sealed record SourceRepository(string Provider, string Owner, string Name, string DefaultBranch, string? Url = null, bool IsPrivate = false)
{
    public RepositoryId Id => new(Provider, Owner, Name);
}

public sealed record SourceBranch(string Name, string HeadSha, string Author, DateTimeOffset UpdatedAt, bool IsDefault, string? HeadMessage = null);
public sealed record SourceCommit(string Sha, string Message, string Author, DateTimeOffset AuthoredAt, string Reference);
public sealed record SourceTree(string Reference, string Path, IReadOnlyList<SourceTreeEntry> Entries);
public sealed record SourceTreeEntry(string Path, string Name, string Kind, long Size, string Sha);
public sealed record SourceFile(string Path, string Reference, string Sha, long Size, string? Content, bool IsBinary);
public sealed record SourceDiff(string BaseSha, string HeadSha, IReadOnlyList<SourceDiffFile> Files)
{
    public int Additions => Files.Sum(file => file.Additions);
    public int Deletions => Files.Sum(file => file.Deletions);
}
public sealed record SourceDiffFile(string Path, string Patch, int Additions, int Deletions, string Status = "modified", string? PreviousPath = null);
public sealed record MergeRequest(string ExternalId, string Method = "merge", string? CommitTitle = null);
public sealed record MergeResult(bool Merged, string? CommitSha, string Message);

public sealed record ExternalChange(
    string ExternalId, int ExternalNumber, string ExternalUrl, string Title, string Description, string Author,
    string SourceBranch, string TargetBranch, string HeadCommit, string BaseCommit, string Status,
    bool IsDraft, bool IsMergeable, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? MergedAt, DateTimeOffset? ClosedAt, SourceDiff? Diff = null);

public sealed record CreateExternalChange(string Title, string Description, string SourceBranch, string TargetBranch);

public interface ISourceProvider
{
    string Id { get; }
    Task<SourceRepository> ConnectAsync(string repositoryUrl, CancellationToken cancellationToken = default);
    Task<SourceRepository> GetRepositoryAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support repository metadata.");
    Task<IReadOnlyList<SourceBranch>> GetBranchesAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support branch browsing.");
    Task<IReadOnlyList<SourceCommit>> GetCommitsAsync(RepositoryId repositoryId, string branch, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support commit browsing.");
    Task<SourceCommit> GetCommitAsync(RepositoryId repositoryId, string commitSha, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support commit lookup.");
    Task<SourceTree> GetTreeAsync(RepositoryId repositoryId, string reference, string? path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support tree browsing.");
    Task<SourceFile> GetFileAsync(RepositoryId repositoryId, string reference, string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support file browsing.");
    Task<SourceDiff> GetDiffAsync(RepositoryId repositoryId, string sourceReference, string targetReference, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not support diffs.");

    Task<SourcePullRequest> GetPullRequestAsync(SourceRepository repository, string externalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not expose changes through ISourceProvider.");
    Task MergeAsync(SourceRepository repository, string externalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"Provider '{Id}' does not expose changes through ISourceProvider.");
}

public interface IChangeSourceProvider
{
    string Id { get; }
    Task<IReadOnlyList<ExternalChange>> GetChangesAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default);
    Task<ExternalChange> GetChangeAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default);
    Task<ExternalChange> CreateChangeAsync(RepositoryId repositoryId, CreateExternalChange request, CancellationToken cancellationToken = default);
    Task<SourceDiff> GetChangeDiffAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default);
    Task<MergeResult> MergeChangeAsync(RepositoryId repositoryId, MergeRequest request, CancellationToken cancellationToken = default);
    Task<ExternalChange> CloseChangeAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default);
}

public sealed record SourcePullRequest(
    string ExternalId, string Title, string Author, string SourceBranch, string TargetBranch,
    string Description, string CommitSha, IReadOnlyList<SourceDiffFile> Files);

public enum SourceProviderErrorKind { Temporary, Authentication, NotFound, RepositoryState, RateLimited, Conflict }

public sealed class SourceProviderException(SourceProviderErrorKind kind, string message, int? retryAfterSeconds = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public SourceProviderErrorKind Kind { get; } = kind;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed record SourceRepositoryConnection(Guid Id, Guid ProjectId, RepositoryId RepositoryId, string DefaultBranch, string Url, DateTimeOffset ConnectedAt);

public interface ISourceConnectionStore
{
    IReadOnlyList<SourceRepositoryConnection> List(Guid projectId);
    SourceRepositoryConnection? Find(Guid id);
    SourceRepositoryConnection? Find(Guid projectId, RepositoryId repositoryId);
    void Save(SourceRepositoryConnection connection);
}
