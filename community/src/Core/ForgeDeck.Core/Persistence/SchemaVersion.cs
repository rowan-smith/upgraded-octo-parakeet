using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeDeck.Core.Persistence;

public static class SchemaBootstrap
{
    public const int PlatformSchemaVersion = 2;

    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Records schema version in sqlite table forgedeck_schema_versions.
    /// Call after EnsureCreated/EnsureSchema for a given component name.
    /// </summary>
    public static void Record(DbConnection connection, string component, int version)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS forgedeck_schema_versions (
              component TEXT PRIMARY KEY,
              version INTEGER NOT NULL,
              applied_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            INSERT INTO forgedeck_schema_versions(component, version, applied_at)
            VALUES ($c, $v, $t)
            ON CONFLICT(component) DO UPDATE SET version=excluded.version, applied_at=excluded.applied_at;
            """;
        cmd.Parameters.Clear();
        AddParameter(cmd, "$c", component);
        AddParameter(cmd, "$v", version);
        AddParameter(cmd, "$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Opens the context connection if needed and records the component version.</summary>
    public static void Record(DbContext db, string component, int version)
    {
        ArgumentNullException.ThrowIfNull(db);
        db.Database.OpenConnection();
        Record(db.Database.GetDbConnection(), component, version);
    }

    /// <summary>
    /// Creates relational tables when a sentinel table is missing.
    /// Safe under concurrent callers sharing the same SQLite file.
    /// </summary>
    public static void EnsureRelationalTables(DbContext db, string sentinelTable)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentinelTable);

        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();
        var gateKey = $"{conn.DataSource}|{conn.Database}|{sentinelTable}";
        var gate = Gates.GetOrAdd(gateKey, static _ => new object());

        lock (gate)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{sentinelTable.Replace("'", "''")}'";
            var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            if (exists)
            {
                return;
            }

            try
            {
                var creator = (IRelationalDatabaseCreator)db.Database.GetService(typeof(IRelationalDatabaseCreator))!;
                creator.CreateTables();
            }
            catch (Exception ex) when (IsAlreadyExists(ex))
            {
                // Another caller created the schema between the check and CreateTables.
            }
        }
    }

    private static bool IsAlreadyExists(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
