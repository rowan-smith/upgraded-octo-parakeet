using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Context;

namespace ForgeDeck.Core.SourceControl;

public sealed class SourceRepositoryService(ISourceConnectionStore connections, SourceProviderRegistry providers, PlatformContextStore context)
{
    public IReadOnlyList<SourceRepositoryConnection> List() => connections.List(context.Project.Id);
    public SourceRepositoryConnection? Find(Guid id) => connections.Find(id);
    public async Task<SourceRepositoryConnection> ConnectAsync(string providerId, string url, CancellationToken token)
    {
        var repository = await providers.Source(providerId).ConnectAsync(url, token);
        var existing = connections.Find(context.Project.Id, repository.Id);
        if (existing is not null)
        {
            return existing;
        }

        var value = new SourceRepositoryConnection(Guid.NewGuid(), context.Project.Id, repository.Id, repository.DefaultBranch, repository.Url ?? url, DateTimeOffset.UtcNow);
        connections.Save(value); return value;
    }
    public Task<IReadOnlyList<SourceBranch>> BranchesAsync(Guid id, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetBranchesAsync(connection.RepositoryId, token); }
    public Task<IReadOnlyList<SourceCommit>> CommitsAsync(Guid id, string? branch, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetCommitsAsync(connection.RepositoryId, branch ?? connection.DefaultBranch, token); }
    public Task<SourceCommit> CommitAsync(Guid id, string sha, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetCommitAsync(connection.RepositoryId, sha, token); }
    public Task<SourceDiff> DiffAsync(Guid id, string head, string @base, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetDiffAsync(connection.RepositoryId, head, @base, token); }
    public Task<SourceTree> TreeAsync(Guid id, string? reference, string? path, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetTreeAsync(connection.RepositoryId, reference ?? connection.DefaultBranch, path, token); }
    public Task<SourceFile> FileAsync(Guid id, string? reference, string path, CancellationToken token) { var connection = Required(id); return providers.Source(connection.RepositoryId.Provider).GetFileAsync(connection.RepositoryId, reference ?? connection.DefaultBranch, path, token); }
    public Task<IReadOnlyList<ExternalChange>> ExternalChangesAsync(Guid id, CancellationToken token) { var connection = Required(id); return providers.Changes(connection.RepositoryId.Provider).GetChangesAsync(connection.RepositoryId, token); }
    private SourceRepositoryConnection Required(Guid id) => connections.Find(id) ?? throw new KeyNotFoundException("Source repository connection was not found.");
}
