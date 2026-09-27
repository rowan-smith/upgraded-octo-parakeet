using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.SourceControl;

public sealed class EfSourceConnectionStore : ISourceConnectionStore
{
    private readonly IDbContextFactory<PlatformDbContext> _factory;

    public EfSourceConnectionStore(IDbContextFactory<PlatformDbContext> factory)
    {
        _factory = factory;
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public IReadOnlyList<SourceRepositoryConnection> List(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.SourceConnections.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .AsEnumerable()
            .Select(Map)
            .ToList();
    }

    public SourceRepositoryConnection? Find(Guid id)
    {
        using var db = _factory.CreateDbContext();
        var row = db.SourceConnections.AsNoTracking().FirstOrDefault(x => x.Id == id);
        return row is null ? null : Map(row);
    }

    public SourceRepositoryConnection? Find(Guid projectId, RepositoryId repositoryId)
    {
        using var db = _factory.CreateDbContext();
        var row = db.SourceConnections.AsNoTracking().FirstOrDefault(x =>
            x.ProjectId == projectId
            && x.ProviderId == repositoryId.Provider
            && x.Owner == repositoryId.Owner
            && x.RepositoryName == repositoryId.Name);
        return row is null ? null : Map(row);
    }

    public void Save(SourceRepositoryConnection value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.SourceConnections.FirstOrDefault(x =>
            x.ProjectId == value.ProjectId
            && x.ProviderId == value.RepositoryId.Provider
            && x.Owner == value.RepositoryId.Owner
            && x.RepositoryName == value.RepositoryId.Name);

        if (existing is null)
        {
            db.SourceConnections.Add(new SourceConnectionRow
            {
                Id = value.Id,
                ProjectId = value.ProjectId,
                ProviderId = value.RepositoryId.Provider,
                Owner = value.RepositoryId.Owner,
                RepositoryName = value.RepositoryId.Name,
                DefaultBranch = value.DefaultBranch,
                Url = value.Url,
                ConnectedAt = value.ConnectedAt
            });
        }
        else
        {
            existing.DefaultBranch = value.DefaultBranch;
            existing.Url = value.Url;
        }

        db.SaveChanges();
    }

    private static SourceRepositoryConnection Map(SourceConnectionRow row) =>
        new(row.Id, row.ProjectId, new RepositoryId(row.ProviderId, row.Owner, row.RepositoryName),
            row.DefaultBranch, row.Url, row.ConnectedAt);
}
