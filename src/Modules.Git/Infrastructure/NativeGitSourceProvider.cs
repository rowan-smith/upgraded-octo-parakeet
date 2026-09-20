using Modules.Git.Application;
using Platform.Contracts.SourceControl;

namespace Modules.Git.Infrastructure;

public sealed class NativeGitSourceProvider(IGitRepositoryStore repositories) : ISourceProvider
{
    public string Id => "native-git";
    public Task<SourceRepository> ConnectAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        var slug = repositoryUrl.Replace("native://", "", StringComparison.OrdinalIgnoreCase).Trim('/').Split('/').Last();
        var repository = repositories.Find(slug) ?? throw new ArgumentException($"Native repository '{slug}' was not found.");
        return Task.FromResult(new SourceRepository(Id, "northstar", repository.Slug, repository.DefaultBranch));
    }
    public Task<SourcePullRequest> GetPullRequestAsync(SourceRepository source, string externalId, CancellationToken cancellationToken = default)
    {
        var repository = repositories.Find(source.Name) ?? throw new ArgumentException("Native repository was not found.");
        var commit = repository.Commits.First();
        return Task.FromResult(new SourcePullRequest(externalId, $"Native change #{externalId}", commit.Author,
            repository.Branches.First(branch => !branch.IsDefault).Name, repository.DefaultBranch,
            "A native repository change using the same source-provider contract as GitHub.", commit.Sha,
            [new SourceDiffFile("src/Native/Hooks.cs", "@@ -1,2 +1,4 @@\n public sealed class Hooks\n {\n+    public bool Enabled => true;\n }", 1, 0)]));
    }
    public Task MergeAsync(SourceRepository repository, string externalId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
