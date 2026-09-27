using System.Text.Json;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Domain;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Git.Infrastructure;

public sealed class EfGitRepositoryStore : IGitRepositoryStore
{
    public static readonly Guid AtlasRepositoryId = Guid.Parse("30000000-0000-4000-8000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<GitDbContext> _dbFactory;
    private readonly object _schemaGate = new();
    private readonly object _seedGate = new();
    private bool _schemaReady;
    private bool _seeded;

    public EfGitRepositoryStore(IDbContextFactory<GitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        EnsureSchema();
        EnsureSeeded();
    }

    public IReadOnlyList<GitRepository> List()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Repositories.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => r.Payload)
            .AsEnumerable()
            .Select(Deserialize)
            .ToList();
    }

    public GitRepository? Find(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Repositories.AsNoTracking()
            .Where(r => r.Id == id.ToString())
            .Select(r => r.Payload)
            .FirstOrDefault();
        return payload is null ? null : Deserialize(payload);
    }

    public GitRepository? Find(string slug)
    {
        using var db = _dbFactory.CreateDbContext();
        var payload = db.Repositories.AsNoTracking()
            .AsEnumerable()
            .FirstOrDefault(r => r.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
            ?.Payload;
        return payload is null ? null : Deserialize(payload);
    }

    public void Add(GitRepository repository) => Upsert(repository);

    public void Update(GitRepository repository) => Upsert(repository);

    private void Upsert(GitRepository repository)
    {
        using var db = _dbFactory.CreateDbContext();
        var id = repository.Id.ToString();
        var row = db.Repositories.FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            row = new GitRepositoryRow
            {
                Id = id,
                CreatedAt = repository.CreatedAt.ToString("O")
            };
            db.Repositories.Add(row);
        }

        row.Slug = repository.Slug;
        row.Name = repository.Name;
        row.Payload = JsonSerializer.Serialize(repository, Json);
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

    private void EnsureSeeded()
    {
        if (_seeded)
        {
            return;
        }

        lock (_seedGate)
        {
            if (_seeded)
            {
                return;
            }

            using (var db = _dbFactory.CreateDbContext())
            {
                if (!db.Repositories.Any())
                {
                    Upsert(CreateSeed());
                }
            }

            _seeded = true;
        }
    }

    private static GitRepository CreateSeed()
    {
        var repository = new GitRepository { Id = AtlasRepositoryId, Name = "Atlas Native", Slug = "atlas-native" };
        var commit = repository.ReceivePush("main", "Initial platform import", "Maya Chen");
        repository.Tags.Add(new GitTag("v0.1.0", commit.Sha));
        repository.ReceivePush("feat/native-hooks", "Add native webhook contracts", "Jamie Park");
        return repository;
    }

    private static GitRepository Deserialize(string json) =>
        JsonSerializer.Deserialize<GitRepository>(json, Json)
        ?? throw new InvalidOperationException("Corrupt git repository payload.");
}
