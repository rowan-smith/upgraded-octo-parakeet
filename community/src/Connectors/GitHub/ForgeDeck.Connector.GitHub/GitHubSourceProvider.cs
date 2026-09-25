using ForgeDeck.Contracts.SourceControl;

namespace ForgeDeck.Connectors.GitHub;

public sealed class GitHubSourceProvider(GitHubApiClient api) : ISourceProvider, IChangeSourceProvider
{
    public string Id => "github";

    public async Task<SourceRepository> ConnectAsync(string repositoryUrl, CancellationToken token = default) =>
        await GetRepositoryAsync(ParseRepository(repositoryUrl), token);

    public async Task<SourceRepository> GetRepositoryAsync(RepositoryId id, CancellationToken token = default)
    {
        using var document = await api.GetAsync(RepoPath(id), token);
        return GitHubMapping.Repository(document.RootElement);
    }

    public async Task<IReadOnlyList<SourceBranch>> GetBranchesAsync(RepositoryId id, CancellationToken token = default)
    {
        var repository = await GetRepositoryAsync(id, token);
        using var document = await api.GetAsync($"{RepoPath(id)}/branches?per_page=100", token);
        var branches = new List<SourceBranch>();
        foreach (var value in document.RootElement.EnumerateArray())
        {
            var name = value.GetProperty("name").GetString()!;
            string? headMessage = null;
            if (value.TryGetProperty("commit", out var tip)
                && tip.TryGetProperty("commit", out var nested)
                && nested.TryGetProperty("message", out var messageProp))
            {
                headMessage = messageProp.GetString();
            }

            var commit = (await GetCommitsAsync(id, name, token)).FirstOrDefault();
            branches.Add(new(
                name,
                value.GetProperty("commit").GetProperty("sha").GetString()!,
                commit?.Author ?? "Unknown",
                commit?.AuthoredAt ?? DateTimeOffset.MinValue,
                name == repository.DefaultBranch,
                headMessage ?? commit?.Message));
        }
        return branches;
    }

    public async Task<IReadOnlyList<SourceCommit>> GetCommitsAsync(RepositoryId id, string branch, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/commits?sha={Uri.EscapeDataString(branch)}&per_page=50", token);
        return document.RootElement.EnumerateArray().Select(value => GitHubMapping.Commit(value, branch)).ToArray();
    }

    public async Task<SourceCommit> GetCommitAsync(RepositoryId id, string commitSha, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/commits/{Uri.EscapeDataString(commitSha)}", token);
        return GitHubMapping.Commit(document.RootElement, commitSha);
    }

    public async Task<SourceTree> GetTreeAsync(RepositoryId id, string reference, string? path, CancellationToken token = default)
    {
        var normalized = path?.Trim('/') ?? "";
        var suffix = string.IsNullOrEmpty(normalized) ? "" : $"/{EscapePath(normalized)}";
        using var document = await api.GetAsync($"{RepoPath(id)}/contents{suffix}?ref={Uri.EscapeDataString(reference)}", token);
        var entries = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(value => new SourceTreeEntry(value.GetProperty("path").GetString()!, value.GetProperty("name").GetString()!,
                value.GetProperty("type").GetString() == "dir" ? "directory" : "file", value.GetProperty("size").GetInt64(), value.GetProperty("sha").GetString()!)).ToArray()
            : Array.Empty<SourceTreeEntry>();
        return new(reference, normalized, entries);
    }

    public async Task<SourceFile> GetFileAsync(RepositoryId id, string reference, string path, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(reference)}", token);
        var value = document.RootElement;
        var content = GitHubMapping.DecodeContent(value);
        var binary = content is null || content.IndexOf('\0') >= 0;
        return new(path, reference, value.GetProperty("sha").GetString()!, value.GetProperty("size").GetInt64(), binary ? null : content, binary);
    }

    public async Task<SourceDiff> GetDiffAsync(RepositoryId id, string sourceReference, string targetReference, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/compare/{Uri.EscapeDataString(targetReference)}...{Uri.EscapeDataString(sourceReference)}", token);
        var root = document.RootElement;
        return new(root.GetProperty("base_commit").GetProperty("sha").GetString()!, root.GetProperty("merge_base_commit").GetProperty("sha").GetString()!,
            root.GetProperty("files").EnumerateArray().Select(GitHubMapping.DiffFile).ToArray());
    }

    public async Task<IReadOnlyList<ExternalChange>> GetChangesAsync(RepositoryId id, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/pulls?state=all&per_page=50", token);
        return document.RootElement.EnumerateArray().Select(value => GitHubMapping.Change(value)).ToArray();
    }

    public async Task<ExternalChange> GetChangeAsync(RepositoryId id, string externalId, CancellationToken token = default)
    {
        using var document = await api.GetAsync($"{RepoPath(id)}/pulls/{ExternalId(externalId)}", token);
        var diff = await GetChangeDiffAsync(id, externalId, token);
        return GitHubMapping.Change(document.RootElement, diff);
    }

    public async Task<ExternalChange> CreateChangeAsync(RepositoryId id, CreateExternalChange request, CancellationToken token = default)
    {
        using var document = await api.PostAsync($"{RepoPath(id)}/pulls", new { title = request.Title, body = request.Description, head = request.SourceBranch, @base = request.TargetBranch }, token);
        return GitHubMapping.Change(document.RootElement);
    }

    public async Task<SourceDiff> GetChangeDiffAsync(RepositoryId id, string externalId, CancellationToken token = default)
    {
        using var change = await api.GetAsync($"{RepoPath(id)}/pulls/{ExternalId(externalId)}", token);
        using var files = await api.GetAsync($"{RepoPath(id)}/pulls/{ExternalId(externalId)}/files?per_page=100", token);
        return new(change.RootElement.GetProperty("base").GetProperty("sha").GetString()!, change.RootElement.GetProperty("head").GetProperty("sha").GetString()!,
            files.RootElement.EnumerateArray().Select(GitHubMapping.DiffFile).ToArray());
    }

    public async Task<MergeResult> MergeChangeAsync(RepositoryId id, MergeRequest request, CancellationToken token = default)
    {
        using var document = await api.PutAsync($"{RepoPath(id)}/pulls/{ExternalId(request.ExternalId)}/merge", new { merge_method = request.Method, commit_title = request.CommitTitle }, token);
        var root = document.RootElement;
        return new(root.GetProperty("merged").GetBoolean(), root.TryGetProperty("sha", out var sha) ? sha.GetString() : null, root.GetProperty("message").GetString() ?? "Merge completed.");
    }

    public async Task<ExternalChange> CloseChangeAsync(RepositoryId id, string externalId, CancellationToken token = default)
    {
        using var document = await api.PatchAsync($"{RepoPath(id)}/pulls/{ExternalId(externalId)}", new { state = "closed" }, token);
        return GitHubMapping.Change(document.RootElement);
    }

    public async Task<SourcePullRequest> GetPullRequestAsync(SourceRepository repository, string externalId, CancellationToken token = default)
    {
        var change = await GetChangeAsync(repository.Id, externalId, token);
        return new(change.ExternalId, change.Title, change.Author, change.SourceBranch, change.TargetBranch, change.Description, change.HeadCommit, change.Diff?.Files ?? []);
    }

    public async Task MergeAsync(SourceRepository repository, string externalId, CancellationToken token = default) =>
        _ = await MergeChangeAsync(repository.Id, new(externalId), token);

    private static string RepoPath(RepositoryId id) => $"repos/{Uri.EscapeDataString(id.Owner)}/{Uri.EscapeDataString(id.Name)}";
    private static string EscapePath(string path) => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
    private static int ExternalId(string value) => int.TryParse(value, out var id) ? id : throw new ArgumentException("GitHub change reference must be a pull request number.");
    private static RepositoryId ParseRepository(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (!normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"https://{normalized}";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Use a GitHub repository URL such as https://github.com/owner/repository.");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException("The GitHub URL must identify one repository.");
        }

        return new("github", parts[0], parts[1]);
    }
}
