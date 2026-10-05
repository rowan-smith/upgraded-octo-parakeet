using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Application;

public interface IChangeRepository
{
    IReadOnlyList<Change> List();
    Change? Find(Guid id);
    Change? Find(string owner, string repository, string externalId);
    void Add(Change change);
    void Update(Change change);

    /// <summary>
    /// Loads, mutates, and saves a change under a per-id lock so concurrent event
    /// handlers cannot clobber each other's payload writes (lost-update).
    /// </summary>
    Change? Mutate(Guid id, Action<Change> mutation);
}
