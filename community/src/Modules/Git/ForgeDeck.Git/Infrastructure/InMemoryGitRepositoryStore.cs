using ForgeDeck.Git.Application;
using ForgeDeck.Git.Domain;

namespace ForgeDeck.Git.Infrastructure;

public sealed class InMemoryGitRepositoryStore : IGitRepositoryStore
{
    public static readonly Guid AtlasRepositoryId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private readonly List<GitRepository> _repositories = [CreateSeed()];
    private readonly object _gate = new();
    public IReadOnlyList<GitRepository> List() { lock (_gate)
        {
            return _repositories.ToArray();
        }
    }
    public GitRepository? Find(Guid id) { lock (_gate)
        {
            return _repositories.FirstOrDefault(repository => repository.Id == id);
        }
    }
    public GitRepository? Find(string slug) { lock (_gate)
        {
            return _repositories.FirstOrDefault(repository => repository.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        }
    }
    public void Add(GitRepository repository) { lock (_gate)
        {
            _repositories.Add(repository);
        }
    }
    public void Update(GitRepository repository) { lock (_gate) { } }
    private static GitRepository CreateSeed()
    {
        var repository = new GitRepository { Id = AtlasRepositoryId, Name = "Atlas Native", Slug = "atlas-native" };
        var commit = repository.ReceivePush("main", "Initial platform import", "Maya Chen");
        repository.Tags.Add(new GitTag("v0.1.0", commit.Sha));
        repository.ReceivePush("feat/native-hooks", "Add native webhook contracts", "Jamie Park");
        return repository;
    }
}
