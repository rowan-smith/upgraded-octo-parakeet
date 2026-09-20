using Modules.Git.Application;
using Platform.Contracts.SourceControl;

namespace Modules.Git.Infrastructure;

public sealed class NativeGitSourceProvider(IGitRepositoryStore repositories) : ISourceProvider, IChangeSourceProvider
{
    public string Id => "native-git";

    public Task<SourceRepository> ConnectAsync(string repositoryUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToSource(RequireByUrl(repositoryUrl)));

    public Task<SourceRepository> GetRepositoryAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToSource(Require(repositoryId.Name)));

    public Task<IReadOnlyList<SourceBranch>> GetBranchesAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        return Task.FromResult<IReadOnlyList<SourceBranch>>(repository.Branches
            .Select(branch =>
            {
                var tip = repository.Commits.FirstOrDefault(c => c.Sha == branch.HeadSha);
                return new SourceBranch(
                    branch.Name,
                    branch.HeadSha,
                    tip?.Author ?? "Unknown",
                    tip?.CreatedAt ?? repository.CreatedAt,
                    branch.IsDefault,
                    tip?.Message);
            }).ToArray());
    }

    public Task<IReadOnlyList<SourceCommit>> GetCommitsAsync(RepositoryId repositoryId, string branch, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        return Task.FromResult<IReadOnlyList<SourceCommit>>(repository.Commits
            .Select(commit => new SourceCommit(commit.Sha, commit.Message, commit.Author, commit.CreatedAt, branch)).ToArray());
    }

    public Task<SourceCommit> GetCommitAsync(RepositoryId repositoryId, string commitSha, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        var commit = repository.Commits.FirstOrDefault(value => value.Sha.StartsWith(commitSha, StringComparison.OrdinalIgnoreCase))
            ?? throw new SourceProviderException(SourceProviderErrorKind.NotFound, $"Commit '{commitSha}' was not found.");
        return Task.FromResult(new SourceCommit(commit.Sha, commit.Message, commit.Author, commit.CreatedAt, commit.Sha));
    }

    public Task<SourceTree> GetTreeAsync(RepositoryId repositoryId, string reference, string? path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SourceTree(reference, path ?? "", [new SourceTreeEntry("README.md", "README.md", "file", 120, "readme")]));

    public Task<SourceFile> GetFileAsync(RepositoryId repositoryId, string reference, string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SourceFile(path, reference, "readme", 120, "# Native repository\n", false));

    public Task<SourceDiff> GetDiffAsync(RepositoryId repositoryId, string sourceReference, string targetReference, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        return Task.FromResult(new SourceDiff(targetReference, repository.Commits.First().Sha, SampleDiff()));
    }

    public Task<IReadOnlyList<ExternalChange>> GetChangesAsync(RepositoryId repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExternalChange>>([BuildChange(Require(repositoryId.Name), "77")]);

    public Task<ExternalChange> GetChangeAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildChange(Require(repositoryId.Name), externalId));

    public Task<ExternalChange> CreateChangeAsync(RepositoryId repositoryId, CreateExternalChange request, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        return Task.FromResult(BuildChange(repository, (repository.Commits.Count + 100).ToString(), request.Title, request.Description, request.SourceBranch, request.TargetBranch));
    }

    public Task<SourceDiff> GetChangeDiffAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        return Task.FromResult(new SourceDiff(repository.Branches.First(b => b.IsDefault).HeadSha, repository.Commits.First().Sha, SampleDiff()));
    }

    public Task<MergeResult> MergeChangeAsync(RepositoryId repositoryId, MergeRequest request, CancellationToken cancellationToken = default)
    {
        var repository = Require(repositoryId.Name);
        var commit = repository.ReceivePush(repository.DefaultBranch, $"Merge change #{request.ExternalId}", "Maya Chen");
        return Task.FromResult(new MergeResult(true, commit.Sha, "Merged."));
    }

    public Task<ExternalChange> CloseChangeAsync(RepositoryId repositoryId, string externalId, CancellationToken cancellationToken = default)
    {
        var change = BuildChange(Require(repositoryId.Name), externalId);
        return Task.FromResult(change with { Status = "Closed", ClosedAt = DateTimeOffset.UtcNow, IsMergeable = false });
    }

    public Task<SourcePullRequest> GetPullRequestAsync(SourceRepository source, string externalId, CancellationToken cancellationToken = default)
    {
        var change = BuildChange(Require(source.Name), externalId);
        return Task.FromResult(new SourcePullRequest(change.ExternalId, change.Title, change.Author, change.SourceBranch, change.TargetBranch,
            change.Description, change.HeadCommit, change.Diff?.Files ?? []));
    }

    public async Task MergeAsync(SourceRepository repository, string externalId, CancellationToken cancellationToken = default) =>
        _ = await MergeChangeAsync(repository.Id, new(externalId), cancellationToken);

    private static ExternalChange BuildChange(Modules.Git.Domain.GitRepository repository, string externalId, string? title = null, string? description = null, string? source = null, string? target = null)
    {
        var head = repository.Commits.First();
        var feature = repository.Branches.FirstOrDefault(branch => !branch.IsDefault)?.Name ?? "feature/native";
        var diff = new SourceDiff(repository.Branches.First(b => b.IsDefault).HeadSha, head.Sha, SampleDiff());
        return new(externalId, int.TryParse(externalId, out var number) ? number : 0, $"native://northstar/{repository.Slug}/changes/{externalId}",
            title ?? $"Native change #{externalId}", description ?? "A native repository change using the same source-provider contract as GitHub.",
            head.Author, source ?? feature, target ?? repository.DefaultBranch, head.Sha, repository.Branches.First(b => b.IsDefault).HeadSha,
            "Open", false, true, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow, null, null, diff);
    }

    private static IReadOnlyList<SourceDiffFile> SampleDiff() =>
        [new("src/Native/Hooks.cs", "@@ -1,2 +1,4 @@\n public sealed class Hooks\n {\n+    public bool Enabled => true;\n }", 1, 0, "modified")];

    private static SourceRepository ToSource(Modules.Git.Domain.GitRepository repository) =>
        new("native-git", "northstar", repository.Slug, repository.DefaultBranch, $"native://northstar/{repository.Slug}");

    private Modules.Git.Domain.GitRepository Require(string slug) =>
        repositories.Find(slug) ?? throw new SourceProviderException(SourceProviderErrorKind.NotFound, $"Native repository '{slug}' was not found.");

    private Modules.Git.Domain.GitRepository RequireByUrl(string repositoryUrl)
    {
        var slug = repositoryUrl.Replace("native://", "", StringComparison.OrdinalIgnoreCase).Trim('/').Split('/').Last();
        return Require(slug);
    }
}
