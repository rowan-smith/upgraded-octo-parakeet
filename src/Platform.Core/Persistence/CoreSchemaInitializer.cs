namespace Platform.Core.Persistence;

public sealed class CoreSchemaInitializer(IDbConnectionFactory connections)
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
                CREATE TABLE IF NOT EXISTS core_integrations (
                    provider_id TEXT PRIMARY KEY,
                    protected_secret TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_source_connections (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    provider_id TEXT NOT NULL,
                    owner TEXT NOT NULL,
                    repository_name TEXT NOT NULL,
                    default_branch TEXT NOT NULL,
                    url TEXT NOT NULL,
                    connected_at TEXT NOT NULL,
                    UNIQUE(project_id, provider_id, owner, repository_name)
                );
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
