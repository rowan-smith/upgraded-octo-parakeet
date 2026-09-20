using Modules.Git.Domain;

namespace Modules.Git.Application;

public interface IGitRepositoryStore
{
    IReadOnlyList<GitRepository> List();
    GitRepository? Find(Guid id);
    GitRepository? Find(string slug);
    void Add(GitRepository repository);
    void Update(GitRepository repository);
}
