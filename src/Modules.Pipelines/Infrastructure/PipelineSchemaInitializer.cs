using Platform.Core.Persistence;

namespace Modules.Pipelines.Infrastructure;

public sealed class PipelineSchemaInitializer(IDbConnectionFactory connections)
{
    private readonly object _gate = new();
    private bool _initialized;

    public void EnsureCreated()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            using var connection = connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS pipelines_definitions (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    version INTEGER NOT NULL,
                    payload TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_pipelines_definitions_project ON pipelines_definitions(project_id, name);

                CREATE TABLE IF NOT EXISTS pipelines_definition_versions (
                    definition_id TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    payload TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    PRIMARY KEY (definition_id, version)
                );

                CREATE TABLE IF NOT EXISTS pipelines_runs (
                    id TEXT PRIMARY KEY,
                    definition_id TEXT NOT NULL,
                    change_id TEXT NULL,
                    commit_sha TEXT NOT NULL,
                    status TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_pipelines_runs_change ON pipelines_runs(change_id, created_at DESC);
                CREATE INDEX IF NOT EXISTS ix_pipelines_runs_definition ON pipelines_runs(definition_id, created_at DESC);
                CREATE INDEX IF NOT EXISTS ix_pipelines_runs_status ON pipelines_runs(status);

                CREATE TABLE IF NOT EXISTS pipelines_runners (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    token_hash TEXT NOT NULL UNIQUE,
                    status TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    last_heartbeat TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS pipelines_registration_tokens (
                    id TEXT PRIMARY KEY,
                    token_hash TEXT NOT NULL UNIQUE,
                    expires_at TEXT NOT NULL,
                    consumed_at TEXT NULL,
                    created_by TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
