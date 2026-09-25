namespace ForgeDeck.Contracts.SourceControl;

public sealed record LocalRemoteInfo(string Name, string Url);

public sealed record DetectedGitHubRepository(string Owner, string Name, string Url)
{
    public string FullName => $"{Owner}/{Name}";
}

public sealed record LocalRemoteAheadBehind(int Ahead, int Behind);

public sealed record LocalRepositoryInfo(
    string Path,
    string Root,
    bool IsGitRepository,
    string? CurrentBranch,
    string? HeadSha,
    int ModifiedFileCount,
    bool IsClean,
    IReadOnlyList<LocalRemoteInfo> Remotes,
    string? OriginUrl,
    DetectedGitHubRepository? DetectedGitHub,
    LocalRemoteAheadBehind? AheadBehind);

public sealed record LocalRepositoryAssociation(
    Guid ProjectId,
    string Path,
    string Root,
    DateTimeOffset AssociatedAt);

public interface ILocalRepositoryStore
{
    LocalRepositoryAssociation? Find(Guid projectId);
    void Save(LocalRepositoryAssociation association);
    void Remove(Guid projectId);
}
