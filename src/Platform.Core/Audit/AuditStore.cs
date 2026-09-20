using System.Data.Common;
using System.Text.Json;
using Platform.Contracts.Audit;
using Platform.Core.Persistence;

namespace Platform.Core.Audit;

/// <summary>SQLite-backed audit log with an in-memory hot cache for recent reads.</summary>
public sealed class AuditStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connections;
    private readonly object _gate = new();
    private readonly List<AuditEvent> _cache = [];
    private bool _loaded;

    public AuditStore(IDbConnectionFactory connections, CoreSchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
    }

    public void Append(AuditEvent auditEvent)
    {
        EnsureLoaded();
        var metadata = auditEvent.Metadata is null ? null : JsonSerializer.Serialize(auditEvent.Metadata, Json);
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO core_audit(id, actor, organisation, project, module, action, resource, timestamp, correlation_id, metadata)
            VALUES($id, $actor, $org, $project, $module, $action, $resource, $ts, $corr, $meta)
            """;
        Add(command, "$id", auditEvent.Id.ToString());
        Add(command, "$actor", auditEvent.Actor);
        Add(command, "$org", auditEvent.Organisation);
        Add(command, "$project", auditEvent.Project);
        Add(command, "$module", auditEvent.Module);
        Add(command, "$action", auditEvent.Action);
        Add(command, "$resource", auditEvent.Resource);
        Add(command, "$ts", auditEvent.Timestamp.ToString("O"));
        Add(command, "$corr", auditEvent.CorrelationId);
        Add(command, "$meta", (object?)metadata ?? DBNull.Value);
        command.ExecuteNonQuery();

        lock (_gate) _cache.Insert(0, auditEvent);
    }

    public IReadOnlyList<AuditEvent> Snapshot(int take = 200)
    {
        EnsureLoaded();
        lock (_gate) return _cache.Take(Math.Max(1, take)).ToArray();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            using var connection = _connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, actor, organisation, project, module, action, resource, timestamp, correlation_id, metadata
                FROM core_audit
                ORDER BY timestamp DESC
                LIMIT 500
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                object? metadata = null;
                if (!reader.IsDBNull(9))
                {
                    try { metadata = JsonSerializer.Deserialize<JsonElement>(reader.GetString(9)); }
                    catch { metadata = reader.GetString(9); }
                }
                _cache.Add(new AuditEvent(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    DateTimeOffset.Parse(reader.GetString(7)),
                    reader.GetString(8),
                    metadata));
            }
            _loaded = true;
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
