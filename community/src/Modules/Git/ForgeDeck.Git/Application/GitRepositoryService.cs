using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Git.Domain;

namespace ForgeDeck.Git.Application;

public sealed class GitRepositoryService(
    IGitRepositoryStore repositories,
    IEventPublisher events,
    IGitObjectStore objectStore)
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
        var key = ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        const string branch = "main";
        const string message = "Initial commit";
        const string author = "Maya Chen";
        var sha = objectStore.CreateCommit(message, author);
        objectStore.UpdateRef(key, $"refs/heads/{branch}", sha);
        repository.ReceivePush(branch, message, author, sha);
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

        var key = ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        var refName = $"refs/heads/{branch}";
        var parent = objectStore.GetRef(key, refName);
        var sha = objectStore.CreateCommit(message, author, parent);
        objectStore.UpdateRef(key, refName, sha);
        var commit = repository.ReceivePush(branch, message, author, sha);
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

    public CloneUrlInfo? GetCloneUrl(Guid repositoryId, string? publicBaseUrl = null)
    {
        var repository = repositories.Find(repositoryId);
        if (repository is null)
        {
            return null;
        }

        var key = ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        var path = objectStore.GetRepositoryPath(key);
        var memory = path.StartsWith("memory://", StringComparison.OrdinalIgnoreCase);
        string? cloneCommand = null;
        string note;
        if (!memory && !string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            var https = $"{publicBaseUrl.TrimEnd('/')}/api/git/repositories/{repository.Id}";
            cloneCommand = $"git clone {https}";
            note = "HTTPS smart-HTTP clone/fetch/push is served from this URL when FileSystem store + git CLI are present.";
        }
        else if (!memory)
        {
            cloneCommand = $"git clone \"{path}\"";
            note = "Local bare-repo path. Prefer HTTPS clone via GET .../clone-url with Host header, or JSON POST .../push.";
        }
        else
        {
            note = "InMemory object store cannot serve smart-HTTP; set Git:ObjectStore=FileSystem.";
        }

        return new CloneUrlInfo(repository.Id, repository.Slug, path, cloneCommand, note);
    }

    public FetchInfo? GetFetchInfo(Guid repositoryId)
    {
        var repository = repositories.Find(repositoryId);
        if (repository is null)
        {
            return null;
        }

        var key = ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        var tips = objectStore.ListRefs(key);
        if (tips.Count == 0)
        {
            var refs = new List<RefInfo>();
            foreach (var branch in repository.Branches)
            {
                var refName = $"refs/heads/{branch.Name}";
                var sha = objectStore.GetRef(key, refName) ?? branch.HeadSha;
                refs.Add(new RefInfo(refName, sha));
            }

            foreach (var tag in repository.Tags)
            {
                var refName = $"refs/tags/{tag.Name}";
                var sha = objectStore.GetRef(key, refName) ?? tag.CommitSha;
                refs.Add(new RefInfo(refName, sha));
            }

            return new FetchInfo(repository.Id, objectStore.GetRepositoryPath(key), refs);
        }

        return new FetchInfo(
            repository.Id,
            objectStore.GetRepositoryPath(key),
            tips.Select(t => new RefInfo(t.Name, t.Sha)).ToArray());
    }

    public CloneUrlInfo? GetHttpsCloneUrl(Guid repositoryId, string baseUrl) =>
        GetCloneUrl(repositoryId, baseUrl);

    /// <summary>Rebuilds branch/tag tips from the object store after an external receive-pack.</summary>
    public void SyncRefsFromObjectStore(Guid repositoryId)
    {
        var repository = repositories.Find(repositoryId);
        if (repository is null)
        {
            return;
        }

        var key = ObjectStoreKey(repository);
        objectStore.EnsureRepository(key);
        var tips = objectStore.ListRefs(key);
        if (tips.Count == 0)
        {
            return;
        }

        foreach (var tip in tips)
        {
            if (tip.Name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                var branchName = tip.Name["refs/heads/".Length..];
                var branch = repository.Branches.FirstOrDefault(b => b.Name == branchName);
                if (branch is null)
                {
                    repository.Branches.Add(new GitBranch(branchName, tip.Sha, branchName == repository.DefaultBranch));
                }
                else if (!string.Equals(branch.HeadSha, tip.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    repository.Branches[repository.Branches.IndexOf(branch)] = branch with { HeadSha = tip.Sha };
                    if (repository.Commits.All(c => !string.Equals(c.Sha, tip.Sha, StringComparison.OrdinalIgnoreCase)))
                    {
                        repository.Commits.Insert(0, new GitCommit(tip.Sha, "smart-http push", "git", DateTimeOffset.UtcNow));
                    }
                }
            }
            else if (tip.Name.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                var tag = tip.Name["refs/tags/".Length..];
                var existing = repository.Tags.FirstOrDefault(t => t.Name == tag);
                if (existing is null)
                {
                    repository.Tags.Add(new GitTag(tag, tip.Sha));
                }
                else
                {
                    repository.Tags[repository.Tags.IndexOf(existing)] = existing with { CommitSha = tip.Sha };
                }
            }
        }

        repositories.Update(repository);
    }

    internal static string ObjectStoreKey(GitRepository repository) => repository.Id.ToString("N");
}

public sealed record CloneUrlInfo(Guid RepositoryId, string Slug, string LocalPath, string? CloneCommand, string Note);
public sealed record RefInfo(string Name, string Sha);
public sealed record FetchInfo(Guid RepositoryId, string LocalPath, IReadOnlyList<RefInfo> Refs);
