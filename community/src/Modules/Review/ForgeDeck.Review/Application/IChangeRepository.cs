using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Application;

public interface IChangeRepository
{
    IReadOnlyList<Change> List();
    Change? Find(Guid id);
    Change? Find(string owner, string repository, string externalId);
    void Add(Change change);
    void Update(Change change);
}
