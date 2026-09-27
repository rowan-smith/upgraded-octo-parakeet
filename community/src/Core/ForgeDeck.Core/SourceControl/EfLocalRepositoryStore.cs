using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.SourceControl;

public sealed class EfLocalRepositoryStore : ILocalRepositoryStore
{
    private readonly IDbContextFactory<PlatformDbContext> _factory;

    public EfLocalRepositoryStore(IDbContextFactory<PlatformDbContext> factory)
    {
        _factory = factory;
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public LocalRepositoryAssociation? Find(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        var row = db.LocalRepositories.AsNoTracking().FirstOrDefault(x => x.ProjectId == projectId);
        return row is null ? null : new LocalRepositoryAssociation(row.ProjectId, row.Path, row.Root, row.AssociatedAt);
    }

    public void Save(LocalRepositoryAssociation association)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.LocalRepositories.Find(association.ProjectId);
        if (existing is null)
        {
            db.LocalRepositories.Add(new LocalRepositoryRow
            {
                ProjectId = association.ProjectId,
                Path = association.Path,
                Root = association.Root,
                AssociatedAt = association.AssociatedAt
            });
        }
        else
        {
            existing.Path = association.Path;
            existing.Root = association.Root;
            existing.AssociatedAt = association.AssociatedAt;
        }

        db.SaveChanges();
    }

    public void Remove(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        db.LocalRepositories.Where(x => x.ProjectId == projectId).ExecuteDelete();
    }
}
