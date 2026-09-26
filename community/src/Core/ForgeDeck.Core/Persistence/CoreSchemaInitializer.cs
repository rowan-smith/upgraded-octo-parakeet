using System.Data.Common;

namespace ForgeDeck.Core.Persistence;

public sealed class CoreSchemaInitializer(IDbConnectionFactory connections)
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
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS core_instance (
                    singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
                    state TEXT NOT NULL,
                    initialised_at TEXT,
                    instance_id TEXT,
                    licence_mode TEXT NOT NULL DEFAULT 'None',
                    bootstrap_enabled INTEGER NOT NULL DEFAULT 1,
                    setup_completed_at TEXT
                );
                INSERT OR IGNORE INTO core_instance(singleton, state, licence_mode, bootstrap_enabled) VALUES(1, 'Uninitialised', 'None', 1);
                CREATE TABLE IF NOT EXISTS core_organisation (
                    id TEXT PRIMARY KEY,
                    singleton INTEGER NOT NULL UNIQUE DEFAULT 1 CHECK (singleton = 1),
                    name TEXT NOT NULL,
                    description TEXT,
                    avatar_url TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_users (
                    id TEXT PRIMARY KEY,
                    email TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    password_hash TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_login_at TEXT,
                    onboarding_dismissed_at TEXT
                );
                CREATE TABLE IF NOT EXISTS core_user_profiles (
                    user_id TEXT PRIMARY KEY REFERENCES core_users(id) ON DELETE CASCADE,
                    display_name TEXT NOT NULL,
                    avatar_url TEXT,
                    bio TEXT,
                    job_title TEXT,
                    timezone TEXT,
                    locale TEXT,
                    default_project_id TEXT
                );
                CREATE TABLE IF NOT EXISTS core_memberships (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL UNIQUE REFERENCES core_users(id) ON DELETE CASCADE,
                    role TEXT NOT NULL,
                    status TEXT NOT NULL,
                    joined_at TEXT NOT NULL,
                    invited_by_user_id TEXT REFERENCES core_users(id)
                );
                CREATE TABLE IF NOT EXISTS core_invitations (
                    id TEXT PRIMARY KEY,
                    email TEXT NOT NULL COLLATE NOCASE,
                    role TEXT NOT NULL,
                    token_hash TEXT NOT NULL UNIQUE,
                    invited_by_user_id TEXT NOT NULL REFERENCES core_users(id),
                    expires_at TEXT NOT NULL,
                    accepted_at TEXT,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_access_roles (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    description TEXT,
                    is_system INTEGER NOT NULL DEFAULT 0,
                    permissions_json TEXT NOT NULL DEFAULT '[]',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_teams (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    description TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    role_id TEXT
                );
                CREATE TABLE IF NOT EXISTS core_team_memberships (
                    id TEXT PRIMARY KEY,
                    team_id TEXT NOT NULL REFERENCES core_teams(id) ON DELETE CASCADE,
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    joined_at TEXT NOT NULL,
                    UNIQUE(team_id, user_id)
                );
                CREATE TABLE IF NOT EXISTS core_projects (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    key TEXT NOT NULL,
                    description TEXT,
                    visibility TEXT NOT NULL,
                    created_by_user_id TEXT NOT NULL REFERENCES core_users(id),
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    archived_at TEXT,
                    repository_mode TEXT NOT NULL DEFAULT 'SingleRepository'
                );
                CREATE TABLE IF NOT EXISTS core_project_user_access (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    granted_at TEXT NOT NULL,
                    role_id TEXT,
                    UNIQUE(project_id, user_id)
                );
                CREATE TABLE IF NOT EXISTS core_project_team_access (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    team_id TEXT NOT NULL REFERENCES core_teams(id) ON DELETE CASCADE,
                    granted_at TEXT NOT NULL,
                    role_id TEXT,
                    UNIQUE(project_id, team_id)
                );
                CREATE TABLE IF NOT EXISTS core_repositories (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    name TEXT NOT NULL,
                    slug TEXT NOT NULL COLLATE NOCASE,
                    default_branch TEXT,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    archived_at TEXT,
                    UNIQUE(project_id, slug)
                );
                CREATE TABLE IF NOT EXISTS core_repository_connections (
                    repository_id TEXT PRIMARY KEY REFERENCES core_repositories(id) ON DELETE CASCADE,
                    provider_type TEXT NOT NULL,
                    external_repository_id TEXT,
                    external_owner TEXT NOT NULL,
                    external_name TEXT NOT NULL,
                    clone_url TEXT NOT NULL,
                    web_url TEXT,
                    last_synced_at TEXT,
                    status TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_user_repository_workspaces (
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    repository_id TEXT NOT NULL REFERENCES core_repositories(id) ON DELETE CASCADE,
                    local_path TEXT NOT NULL,
                    last_detected_branch TEXT,
                    last_detected_head TEXT,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY(user_id, repository_id)
                );
                CREATE TABLE IF NOT EXISTS core_sessions (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    token_hash TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    revoked_at TEXT
                );
                CREATE TABLE IF NOT EXISTS core_bootstrap_sessions (
                    id TEXT PRIMARY KEY,
                    token_hash TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL,
                    revoked_at TEXT
                );
                CREATE TABLE IF NOT EXISTS core_licences (
                    id TEXT PRIMARY KEY,
                    licence_id TEXT,
                    customer_id TEXT,
                    mode TEXT NOT NULL,
                    status TEXT NOT NULL,
                    issued_at TEXT,
                    expires_at TEXT,
                    payload TEXT,
                    signature TEXT,
                    installed_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_licence_history (
                    id TEXT PRIMARY KEY,
                    action TEXT NOT NULL,
                    mode TEXT,
                    licence_id TEXT,
                    detail TEXT,
                    at TEXT NOT NULL
                );
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
                CREATE TABLE IF NOT EXISTS core_local_repositories (
                    project_id TEXT PRIMARY KEY,
                    path TEXT NOT NULL,
                    root TEXT NOT NULL,
                    associated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS core_audit (
                    id TEXT PRIMARY KEY,
                    actor TEXT NOT NULL,
                    organisation TEXT NOT NULL,
                    project TEXT NOT NULL,
                    module TEXT NOT NULL,
                    action TEXT NOT NULL,
                    resource TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    correlation_id TEXT NOT NULL,
                    metadata TEXT
                );
                CREATE TABLE IF NOT EXISTS core_user_project_stars (
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    starred_at TEXT NOT NULL,
                    PRIMARY KEY(user_id, project_id)
                );
                CREATE TABLE IF NOT EXISTS core_extensions (
                    extension_id TEXT PRIMARY KEY,
                    type TEXT NOT NULL,
                    runtime_id TEXT,
                    installed_version TEXT,
                    state TEXT NOT NULL,
                    enabled INTEGER NOT NULL DEFAULT 0,
                    installed_at TEXT,
                    installed_by TEXT,
                    updated_at TEXT NOT NULL,
                    last_error TEXT,
                    restart_required INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS core_project_modules (
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    extension_id TEXT NOT NULL,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    PRIMARY KEY(project_id, extension_id)
                );
                CREATE INDEX IF NOT EXISTS ix_core_audit_timestamp ON core_audit(timestamp DESC);
                CREATE INDEX IF NOT EXISTS ix_core_licences_status ON core_licences(status);
                CREATE INDEX IF NOT EXISTS ix_core_user_project_stars_user ON core_user_project_stars(user_id);
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "core_instance", "instance_id", "TEXT");
            EnsureColumn(connection, "core_instance", "licence_mode", "TEXT NOT NULL DEFAULT 'None'");
            EnsureColumn(connection, "core_instance", "bootstrap_enabled", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(connection, "core_instance", "setup_completed_at", "TEXT");
            EnsureColumn(connection, "core_instance", "modules_acknowledged_at", "TEXT");
            EnsureColumn(connection, "core_projects", "repository_mode", "TEXT NOT NULL DEFAULT 'SingleRepository'");
            EnsureColumn(connection, "core_user_profiles", "theme", "TEXT NOT NULL DEFAULT 'system'");
            EnsureColumn(connection, "core_teams", "role_id", "TEXT");
            EnsureColumn(connection, "core_project_user_access", "role_id", "TEXT");
            EnsureColumn(connection, "core_project_team_access", "role_id", "TEXT");
            // Backfill defaults only after columns exist.
            using (var seedId = connection.CreateCommand())
            {
                seedId.CommandText = """
                    UPDATE core_instance
                    SET licence_mode = COALESCE(NULLIF(licence_mode, ''), 'None')
                    WHERE singleton = 1;
                    UPDATE core_instance
                    SET bootstrap_enabled = CASE WHEN state = 'Initialised' THEN 0 ELSE COALESCE(bootstrap_enabled, 1) END
                    WHERE singleton = 1;
                    UPDATE core_projects
                    SET repository_mode = COALESCE(NULLIF(repository_mode, ''), 'SingleRepository')
                    WHERE repository_mode IS NULL OR repository_mode = '';
                    """;
                seedId.ExecuteNonQuery();
            }
            _initialized = true;
        }
    }

    private static void EnsureColumn(DbConnection connection, string table, string column, string definition)
    {
        var exists = false;
        using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"PRAGMA table_info({table})";
            using var reader = probe.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
