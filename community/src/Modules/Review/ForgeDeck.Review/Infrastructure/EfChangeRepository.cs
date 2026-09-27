using System.Text.Json;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Review.Infrastructure;

public sealed class EfChangeRepository : IChangeRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<ReviewDbContext> _dbFactory;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public EfChangeRepository(IDbContextFactory<ReviewDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        EnsureSchema();
    }

    public IReadOnlyList<Change> List()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Changes
            .AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.Payload)
            .AsEnumerable()
            .Select(Deserialize)
            .ToList();
    }

    public Change? Find(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Changes.AsNoTracking().Where(c => c.Id == id.ToString()).Select(c => c.Payload).FirstOrDefault();
        return payload is null ? null : Deserialize(payload);
    }

    public Change? Find(string owner, string repository, string externalId)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Changes.AsNoTracking()
            .Where(c => c.Owner == owner && c.RepositoryName == repository && c.ExternalId == externalId)
            .Select(c => c.Payload)
            .FirstOrDefault();
        return payload is null ? null : Deserialize(payload);
    }

    public void Add(Change change) => Save(change);
    public void Update(Change change) => Save(change);

    private void Save(Change change)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = change.Id.ToString();
        var row = db.Changes.FirstOrDefault(c => c.Id == id);
        if (row is null)
        {
            row = new ChangeRow { Id = id };
            db.Changes.Add(row);
        }

        row.ProjectId = change.ProjectId.ToString();
        row.ProviderId = change.Repository.Provider;
        row.Owner = change.Repository.Owner;
        row.RepositoryName = change.Repository.Name;
        row.ExternalId = change.ExternalId;
        row.Status = change.Status;
        row.UpdatedAt = change.UpdatedAt.ToString("O");
        row.Payload = JsonSerializer.Serialize(change, Json);
        db.SaveChanges();
    }

    private void EnsureSchema()
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaGate)
        {
            if (_schemaReady)
            {
                return;
            }

            using var db = _dbFactory.CreateDbContext();
            db.EnsureSchema();
            _schemaReady = true;
        }
    }

    private static Change Deserialize(string payload) =>
        JsonSerializer.Deserialize<Change>(payload, Json) ?? throw new InvalidOperationException("Stored change is invalid.");
}
