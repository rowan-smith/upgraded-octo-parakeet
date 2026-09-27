using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Git.Domain;

namespace ForgeDeck.Git.Application;

public sealed class GitRepositoryService(IGitRepositoryStore repositories, IEventPublisher events)
{
    public IReadOnlyList<GitRepository> List() => repositories.List();
    public GitRepository? Find(Guid id) => repositories.Find(id);
    public GitRepository? Find(string slug) => repositories.Find(slug);
    public async Task<GitRepository> CreateAsync(string name, CancellationToken token = default)
    {
        var slug = string.Join('-', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Repository name is required.");
        }

        if (repositories.Find(slug) is not null)
        {
            throw new InvalidOperationException("A repository with that slug already exists.");
        }

        var repository = new GitRepository { Name = name.Trim(), Slug = slug };
        repository.ReceivePush("main", "Initial commit", "Maya Chen");
        repositories.Add(repository);
        await events.PublishAsync(
            new GitRepositoryCreatedEvent(repository.Id, "ATL", repository.Slug),
            new PublishOptions
            {
                Actor = new EventActor(ActorType.System, "forgedeck.git", "Git"),
                Publisher = "forgedeck.git"
            },
            token);
        return repository;
    }

    public GitRepository Create(string name) => CreateAsync(name).GetAwaiter().GetResult();

    public async Task<GitCommit?> PushAsync(Guid repositoryId, string branch, string message, string author, CancellationToken token = default)
    {
        var repository = repositories.Find(repositoryId);
        if (repository is null)
        {
            return null;
        }

        var commit = repository.ReceivePush(branch, message, author);
        repositories.Update(repository);
        await events.PublishAsync(
            new GitRepositoryPushEvent(repository.Id, "ATL", repository.Slug, branch, commit.Sha),
            new PublishOptions
            {
                Actor = new EventActor(ActorType.User, author, author),
                Publisher = "forgedeck.git"
            },
            token);
        return commit;
    }
}
