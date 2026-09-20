using Platform.Core.Persistence;

namespace Modules.Review.Infrastructure;

public sealed class ReviewSchemaInitializer(IDbConnectionFactory connections)
{
    private readonly object _gate = new();
    private bool _initialized;
    public void EnsureCreated()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            using var connection = connections.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS review_changes (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    provider_id TEXT NOT NULL,
                    owner TEXT NOT NULL,
                    repository_name TEXT NOT NULL,
                    external_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    UNIQUE(provider_id, owner, repository_name, external_id)
                );
                CREATE INDEX IF NOT EXISTS ix_review_changes_project ON review_changes(project_id, updated_at DESC);
                """;
            command.ExecuteNonQuery(); _initialized = true;
        }
    }
}
