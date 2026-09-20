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
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS core_instance (
                    singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
                    state TEXT NOT NULL,
                    initialised_at TEXT
                );
                INSERT OR IGNORE INTO core_instance(singleton, state) VALUES(1, 'Uninitialised');
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
                CREATE TABLE IF NOT EXISTS core_teams (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    description TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
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
                    archived_at TEXT
                );
                CREATE TABLE IF NOT EXISTS core_project_user_access (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    user_id TEXT NOT NULL REFERENCES core_users(id) ON DELETE CASCADE,
                    granted_at TEXT NOT NULL,
                    UNIQUE(project_id, user_id)
                );
                CREATE TABLE IF NOT EXISTS core_project_team_access (
                    id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES core_projects(id) ON DELETE CASCADE,
                    team_id TEXT NOT NULL REFERENCES core_teams(id) ON DELETE CASCADE,
                    granted_at TEXT NOT NULL,
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
                CREATE INDEX IF NOT EXISTS ix_core_audit_timestamp ON core_audit(timestamp DESC);
                """;
            command.ExecuteNonQuery();
            _initialized = true;
        }
    }
}
