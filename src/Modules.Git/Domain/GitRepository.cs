namespace Modules.Git.Domain;

public sealed record GitCommit(string Sha, string Message, string Author, DateTimeOffset CreatedAt);
public sealed record GitBranch(string Name, string HeadSha, bool IsDefault = false);
public sealed record GitTag(string Name, string CommitSha);

public sealed class GitRepository
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string DefaultBranch { get; init; } = "main";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<GitCommit> Commits { get; } = [];
    public List<GitBranch> Branches { get; } = [];
    public List<GitTag> Tags { get; } = [];

    public GitCommit ReceivePush(string branchName, string message, string author)
    {
        var sha = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()[..7];
        var commit = new GitCommit(sha, message, author, DateTimeOffset.UtcNow);
        Commits.Insert(0, commit);
        var branch = Branches.FirstOrDefault(item => item.Name == branchName);
        if (branch is not null) Branches[Branches.IndexOf(branch)] = branch with { HeadSha = sha };
        else Branches.Add(new GitBranch(branchName, sha, branchName == DefaultBranch));
        return commit;
    }
}
