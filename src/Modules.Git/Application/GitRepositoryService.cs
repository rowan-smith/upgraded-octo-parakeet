using Modules.Git.Domain;
using Platform.Contracts.Events;

namespace Modules.Git.Application;

public sealed class GitRepositoryService(IGitRepositoryStore repositories, IEventPublisher events)
{
    public IReadOnlyList<GitRepository> List() => repositories.List();
    public GitRepository? Find(Guid id) => repositories.Find(id);
    public GitRepository? Find(string slug) => repositories.Find(slug);
    public GitRepository Create(string name)
    {
        var slug = string.Join('-', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Repository name is required.");
        if (repositories.Find(slug) is not null) throw new InvalidOperationException("A repository with that slug already exists.");
        var repository = new GitRepository { Name = name.Trim(), Slug = slug };
        repository.ReceivePush("main", "Initial commit", "Maya Chen"); repositories.Add(repository); return repository;
    }
    public async Task<GitCommit?> PushAsync(Guid repositoryId, string branch, string message, string author, CancellationToken token = default)
    {
        var repository = repositories.Find(repositoryId); if (repository is null) return null;
        var commit = repository.ReceivePush(branch, message, author); repositories.Update(repository);
        await events.PublishAsync(new PushReceived(repository.Id, "ATL", repository.Slug, branch, commit.Sha), token);
        return commit;
    }
}
