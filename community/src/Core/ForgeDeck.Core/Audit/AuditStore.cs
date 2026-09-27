using System.Text.Json;
using ForgeDeck.Contracts.Audit;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.Audit;

/// <summary>EF Core-backed audit log with an in-memory hot cache for recent reads.</summary>
public sealed class AuditStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<PlatformDbContext> _factory;
    private readonly object _gate = new();
    private readonly List<AuditEvent> _cache = [];
    private bool _loaded;

    public AuditStore(IDbContextFactory<PlatformDbContext> factory)
    {
        _factory = factory;
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public void Append(AuditEvent auditEvent)
    {
        EnsureLoaded();
        var metadata = auditEvent.Metadata is null ? null : JsonSerializer.Serialize(auditEvent.Metadata, Json);
        using var db = _factory.CreateDbContext();
        db.AuditEvents.Add(new AuditEventRow
        {
            Id = auditEvent.Id,
            Actor = auditEvent.Actor,
            Organisation = auditEvent.Organisation,
            Project = auditEvent.Project,
            Module = auditEvent.Module,
            Action = auditEvent.Action,
            Resource = auditEvent.Resource,
            Timestamp = auditEvent.Timestamp,
            CorrelationId = auditEvent.CorrelationId,
            MetadataJson = metadata
        });
        db.SaveChanges();

        lock (_gate)
        {
            _cache.Insert(0, auditEvent);
        }
    }

    public IReadOnlyList<AuditEvent> Snapshot(int take = 200)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _cache.Take(Math.Max(1, take)).ToArray();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            using var db = _factory.CreateDbContext();
            var rows = db.AuditEvents.AsNoTracking()
                .OrderByDescending(x => x.Timestamp)
                .Take(500)
                .ToList();

            foreach (var row in rows)
            {
                object? metadata = null;
                if (row.MetadataJson is not null)
                {
                    try { metadata = JsonSerializer.Deserialize<JsonElement>(row.MetadataJson); }
                    catch { metadata = row.MetadataJson; }
                }

                _cache.Add(new AuditEvent(
                    row.Id,
                    row.Actor,
                    row.Organisation,
                    row.Project,
                    row.Module,
                    row.Action,
                    row.Resource,
                    row.Timestamp,
                    row.CorrelationId,
                    metadata));
            }

            _loaded = true;
        }
    }
}
