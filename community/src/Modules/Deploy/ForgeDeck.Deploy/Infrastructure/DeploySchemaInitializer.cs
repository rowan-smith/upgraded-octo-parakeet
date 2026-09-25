using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Deploy.Infrastructure;

public sealed class DeploySchemaInitializer(IDbConnectionFactory connections)
{
    private readonly object _gate = new();
    private bool _initialized;

    public void EnsureCreated()
    {
        if (_initialized)
        {
            return;
        }

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            using var connection = connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS deploy_environments (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_deploy_environments_project ON deploy_environments(project_id, name);

                CREATE TABLE IF NOT EXISTS deploy_deployments (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    environment_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_deploy_deployments_env ON deploy_deployments(environment_id, created_at DESC);
                CREATE INDEX IF NOT EXISTS ix_deploy_deployments_project ON deploy_deployments(project_id, created_at DESC);
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
