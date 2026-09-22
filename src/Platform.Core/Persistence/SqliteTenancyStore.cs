using System.Data.Common;
using Platform.Core.Domain;

namespace Platform.Core.Persistence;

public sealed class SqliteTenancyStore : ITenancyStore
{
    private readonly IDbConnectionFactory _connections;

    public SqliteTenancyStore(IDbConnectionFactory connections, CoreSchemaInitializer schema)
    {
        _connections = connections;
        schema.EnsureCreated();
    }

    public InstanceConfiguration GetInstance()
    {
        using var connection = _connections.Open();
        using var command = Command(connection, """
            SELECT state, initialised_at, instance_id, licence_mode, bootstrap_enabled, setup_completed_at
            FROM core_instance WHERE singleton=1
            """);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new InstanceConfiguration();
        var instanceIdRaw = Text(reader, 2);
        var instanceId = Guid.TryParse(instanceIdRaw, out var parsed) ? parsed : Guid.Empty;
        var licenceModeRaw = Text(reader, 3) ?? "None";
        if (!Enum.TryParse<LicenceMode>(licenceModeRaw, true, out var licenceMode)) licenceMode = LicenceMode.None;
        var bootstrap = reader.IsDBNull(4) || reader.GetInt64(4) != 0;
        return new InstanceConfiguration
        {
            State = Enum.Parse<InstanceState>(reader.GetString(0)),
            InitialisedAt = Date(reader, 1),
            InstanceId = instanceId,
            LicenceMode = licenceMode,
            BootstrapEnabled = bootstrap,
            SetupCompletedAt = Date(reader, 5)
        };
    }

    public void EnsureInstanceId()
    {
        var instance = GetInstance();
        if (instance.InstanceId != Guid.Empty) return;
        instance.InstanceId = Guid.NewGuid();
        SaveInstance(instance);
    }

    public void SaveInstance(InstanceConfiguration configuration)
    {
        Execute("""
            UPDATE core_instance SET
                state=$state,
                initialised_at=$initialised,
                instance_id=$instanceId,
                licence_mode=$licenceMode,
                bootstrap_enabled=$bootstrap,
                setup_completed_at=$setupCompleted
            WHERE singleton=1
            """,
            ("$state", configuration.State),
            ("$initialised", configuration.InitialisedAt),
            ("$instanceId", configuration.InstanceId == Guid.Empty ? null : configuration.InstanceId),
            ("$licenceMode", configuration.LicenceMode),
            ("$bootstrap", configuration.BootstrapEnabled ? 1 : 0),
            ("$setupCompleted", configuration.SetupCompletedAt));
    }

    public Organisation? GetOrganisation()
    {
        using var connection = _connections.Open();
        using var command = Command(connection, "SELECT id,name,description,avatar_url,created_at,updated_at FROM core_organisation LIMIT 1");
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapOrganisation(reader) : null;
    }

    public void Bootstrap(Organisation organisation, UserAccount user, UserProfile profile, OrganisationMembership membership)
    {
        using var connection = _connections.Open();
        using var transaction = connection.BeginTransaction();
        using var state = Command(connection, "SELECT state FROM core_instance WHERE singleton=1", transaction);
        if (!string.Equals((string?)state.ExecuteScalar(), InstanceState.Uninitialised.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");

        var instanceId = Guid.NewGuid();
        Execute(connection, InsertOrganisation, transaction, OrganisationValues(organisation));
        Execute(connection, InsertUser, transaction, UserValues(user));
        Execute(connection, InsertProfile, transaction, ProfileValues(profile));
        Execute(connection, InsertMembership, transaction, MembershipValues(membership));
        Execute(connection, """
            UPDATE core_instance SET
                state='Initialised',
                initialised_at=$at,
                instance_id=COALESCE(NULLIF(instance_id,''), $instanceId),
                licence_mode=CASE WHEN licence_mode IS NULL OR licence_mode='None' THEN 'Community' ELSE licence_mode END,
                bootstrap_enabled=0
            WHERE singleton=1 AND state='Uninitialised'
            """, transaction,
            ("$at", DateTimeOffset.UtcNow),
            ("$instanceId", instanceId));
        using var confirm = Command(connection, "SELECT changes()", transaction);
        if (Convert.ToInt32(confirm.ExecuteScalar()) != 1)
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        transaction.Commit();
    }

    public void CreateOwner(UserAccount user, UserProfile profile, OrganisationMembership membership)
    {
        using var connection = _connections.Open();
        using var transaction = connection.BeginTransaction();
        using var state = Command(connection, "SELECT state FROM core_instance WHERE singleton=1", transaction);
        if (!string.Equals((string?)state.ExecuteScalar(), InstanceState.Uninitialised.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        using var org = Command(connection, "SELECT COUNT(1) FROM core_organisation", transaction);
        if (Convert.ToInt32(org.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Organisation must be configured before creating an owner.");

        Execute(connection, InsertUser, transaction, UserValues(user));
        Execute(connection, InsertProfile, transaction, ProfileValues(profile));
        Execute(connection, InsertMembership, transaction, MembershipValues(membership));
        Execute(connection, """
            UPDATE core_instance SET state='Initialised', initialised_at=$at, bootstrap_enabled=0
            WHERE singleton=1 AND state='Uninitialised'
            """, transaction, ("$at", DateTimeOffset.UtcNow));
        using var confirm = Command(connection, "SELECT changes()", transaction);
        if (Convert.ToInt32(confirm.ExecuteScalar()) != 1)
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        Execute(connection, "UPDATE core_bootstrap_sessions SET revoked_at=$at WHERE revoked_at IS NULL", transaction, ("$at", DateTimeOffset.UtcNow));
        transaction.Commit();
    }

    public void SaveOrganisation(Organisation value)
    {
        var existing = GetOrganisation();
        if (existing is not null && existing.Id != value.Id)
            throw new InvalidOperationException("A ForgeDeck installation can contain only one organisation.");
        Execute(UpsertOrganisation, OrganisationValues(value));
    }
    public UserAccount? FindUser(Guid id) => QueryOne("SELECT * FROM core_users WHERE id=$value", MapUser, ("$value", id));
    public UserAccount? FindUserByEmail(string email) => QueryOne("SELECT * FROM core_users WHERE email=$value COLLATE NOCASE", MapUser, ("$value", email));
    public UserAccount? FindUserByUsername(string username) => QueryOne("SELECT * FROM core_users WHERE username=$value COLLATE NOCASE", MapUser, ("$value", username));
    public IReadOnlyList<UserAccount> ListUsers() => QueryMany("SELECT * FROM core_users ORDER BY email", MapUser);
    public void SaveUser(UserAccount value) => Execute(UpsertUser, UserValues(value));
    public UserProfile? GetProfile(Guid userId) => QueryOne("SELECT * FROM core_user_profiles WHERE user_id=$value", MapProfile, ("$value", userId));
    public void SaveProfile(UserProfile value) => Execute(UpsertProfile, ProfileValues(value));

    public OrganisationMembership? GetMembership(Guid userId) => QueryOne("SELECT * FROM core_memberships WHERE user_id=$value", MapMembership, ("$value", userId));
    public IReadOnlyList<OrganisationMembership> ListMemberships() => QueryMany("SELECT * FROM core_memberships ORDER BY joined_at", MapMembership);
    public void SaveMembership(OrganisationMembership value) => Execute(UpsertMembership, MembershipValues(value));
    public void DeleteMembership(Guid userId) => Execute("DELETE FROM core_memberships WHERE user_id=$value", ("$value", userId));

    public void SaveInvitation(Invitation value) => Execute("""
        INSERT INTO core_invitations(id,email,role,token_hash,invited_by_user_id,expires_at,accepted_at,created_at)
        VALUES($id,$email,$role,$token,$inviter,$expires,$accepted,$created)
        ON CONFLICT(id) DO UPDATE SET email=excluded.email,role=excluded.role,accepted_at=excluded.accepted_at
        """, ("$id", value.Id), ("$email", value.Email), ("$role", value.Role), ("$token", value.TokenHash),
        ("$inviter", value.InvitedByUserId), ("$expires", value.ExpiresAt), ("$accepted", value.AcceptedAt), ("$created", value.CreatedAt));
    public Invitation? FindInvitationByTokenHash(string tokenHash) => QueryOne("SELECT * FROM core_invitations WHERE token_hash=$value", MapInvitation, ("$value", tokenHash));
    public Invitation? FindInvitation(Guid id) => QueryOne("SELECT * FROM core_invitations WHERE id=$value", MapInvitation, ("$value", id));
    public IReadOnlyList<Invitation> ListInvitations() => QueryMany("SELECT * FROM core_invitations ORDER BY created_at DESC", MapInvitation);

    public void SaveTeam(Team value) => Execute("""
        INSERT INTO core_teams(id,name,slug,description,created_at,updated_at) VALUES($id,$name,$slug,$description,$created,$updated)
        ON CONFLICT(id) DO UPDATE SET name=excluded.name,slug=excluded.slug,description=excluded.description,updated_at=excluded.updated_at
        """, ("$id", value.Id), ("$name", value.Name), ("$slug", value.Slug), ("$description", value.Description), ("$created", value.CreatedAt), ("$updated", value.UpdatedAt));
    public Team? FindTeam(Guid id) => QueryOne("SELECT * FROM core_teams WHERE id=$value", MapTeam, ("$value", id));
    public Team? FindTeamBySlug(string slug) => QueryOne("SELECT * FROM core_teams WHERE slug=$value COLLATE NOCASE", MapTeam, ("$value", slug));
    public IReadOnlyList<Team> ListTeams() => QueryMany("SELECT * FROM core_teams ORDER BY name", MapTeam);
    public void DeleteTeam(Guid id)
    {
        Execute("DELETE FROM core_team_memberships WHERE team_id=$value", ("$value", id));
        Execute("DELETE FROM core_project_team_access WHERE team_id=$value", ("$value", id));
        Execute("DELETE FROM core_teams WHERE id=$value", ("$value", id));
    }
    public void SaveTeamMembership(TeamMembership value) => Execute("""
        INSERT INTO core_team_memberships(id,team_id,user_id,joined_at) VALUES($id,$team,$user,$joined)
        ON CONFLICT(team_id,user_id) DO NOTHING
        """, ("$id", value.Id), ("$team", value.TeamId), ("$user", value.UserId), ("$joined", value.JoinedAt));
    public void DeleteTeamMembership(Guid teamId, Guid userId) =>
        Execute("DELETE FROM core_team_memberships WHERE team_id=$team AND user_id=$user", ("$team", teamId), ("$user", userId));
    public IReadOnlyList<TeamMembership> ListTeamMemberships(Guid teamId) =>
        QueryMany("SELECT id,team_id,user_id,joined_at FROM core_team_memberships WHERE team_id=$value", MapTeamMembership, ("$value", teamId));
    public IReadOnlyList<TeamMembership> ListUserTeamMemberships(Guid userId) =>
        QueryMany("SELECT id,team_id,user_id,joined_at FROM core_team_memberships WHERE user_id=$value", MapTeamMembership, ("$value", userId));

    public void SaveProject(Project value) => Execute("""
        INSERT INTO core_projects(id,name,slug,key,description,visibility,created_by_user_id,created_at,updated_at,archived_at,repository_mode)
        VALUES($id,$name,$slug,$key,$description,$visibility,$creator,$created,$updated,$archived,$repoMode)
        ON CONFLICT(id) DO UPDATE SET name=excluded.name,slug=excluded.slug,key=excluded.key,description=excluded.description,
        visibility=excluded.visibility,updated_at=excluded.updated_at,archived_at=excluded.archived_at,repository_mode=excluded.repository_mode
        """, ProjectValues(value));
    public Project? FindProject(Guid id) => QueryOne("SELECT * FROM core_projects WHERE id=$value", MapProject, ("$value", id));
    public Project? FindProjectBySlug(string slug) => QueryOne("SELECT * FROM core_projects WHERE slug=$value COLLATE NOCASE", MapProject, ("$value", slug));
    public IReadOnlyList<Project> ListProjects() => QueryMany("SELECT * FROM core_projects WHERE archived_at IS NULL ORDER BY name", MapProject);

    public IReadOnlyList<Guid> ListStarredProjectIds(Guid userId) =>
        QueryMany(
            "SELECT project_id FROM core_user_project_stars WHERE user_id=$user ORDER BY starred_at DESC",
            r => Guid.Parse(r.GetString(0)),
            ("$user", userId));

    public bool IsProjectStarred(Guid userId, Guid projectId)
    {
        using var connection = _connections.Open();
        using var command = Command(connection, """
            SELECT 1 FROM core_user_project_stars WHERE user_id=$user AND project_id=$project LIMIT 1
            """, null, ("$user", userId), ("$project", projectId));
        return command.ExecuteScalar() is not null;
    }

    public void StarProject(Guid userId, Guid projectId) =>
        Execute("""
            INSERT INTO core_user_project_stars(user_id, project_id, starred_at)
            VALUES($user, $project, $at)
            ON CONFLICT(user_id, project_id) DO NOTHING
            """,
            ("$user", userId),
            ("$project", projectId),
            ("$at", DateTimeOffset.UtcNow));

    public void UnstarProject(Guid userId, Guid projectId) =>
        Execute(
            "DELETE FROM core_user_project_stars WHERE user_id=$user AND project_id=$project",
            ("$user", userId),
            ("$project", projectId));
    public void DeleteProject(Guid id) => Execute("DELETE FROM core_projects WHERE id=$value", ("$value", id));
    public void SaveProjectUserAccess(ProjectUserAccess value) => Execute("""
        INSERT INTO core_project_user_access(id,project_id,user_id,granted_at) VALUES($id,$project,$user,$granted)
        ON CONFLICT(project_id,user_id) DO NOTHING
        """, ("$id", value.Id), ("$project", value.ProjectId), ("$user", value.UserId), ("$granted", value.GrantedAt));
    public void DeleteProjectUserAccess(Guid projectId, Guid userId) =>
        Execute("DELETE FROM core_project_user_access WHERE project_id=$project AND user_id=$user", ("$project", projectId), ("$user", userId));
    public IReadOnlyList<ProjectUserAccess> ListProjectUserAccess(Guid projectId) =>
        QueryMany("SELECT id,project_id,user_id,granted_at FROM core_project_user_access WHERE project_id=$value", MapProjectUserAccess, ("$value", projectId));
    public bool HasProjectUserAccess(Guid projectId, Guid userId) =>
        QueryOne("SELECT id,project_id,user_id,granted_at FROM core_project_user_access WHERE project_id=$project AND user_id=$user LIMIT 1", MapProjectUserAccess, ("$project", projectId), ("$user", userId)) is not null;
    public void SaveProjectTeamAccess(ProjectTeamAccess value) => Execute("""
        INSERT INTO core_project_team_access(id,project_id,team_id,granted_at) VALUES($id,$project,$team,$granted)
        ON CONFLICT(project_id,team_id) DO NOTHING
        """, ("$id", value.Id), ("$project", value.ProjectId), ("$team", value.TeamId), ("$granted", value.GrantedAt));
    public void DeleteProjectTeamAccess(Guid projectId, Guid teamId) =>
        Execute("DELETE FROM core_project_team_access WHERE project_id=$project AND team_id=$team", ("$project", projectId), ("$team", teamId));
    public IReadOnlyList<ProjectTeamAccess> ListProjectTeamAccess(Guid projectId) =>
        QueryMany("SELECT id,project_id,team_id,granted_at FROM core_project_team_access WHERE project_id=$value", MapProjectTeamAccess, ("$value", projectId));
    public bool HasProjectTeamAccess(Guid projectId, Guid teamId) =>
        QueryOne("SELECT id,project_id,team_id,granted_at FROM core_project_team_access WHERE project_id=$project AND team_id=$team LIMIT 1", MapProjectTeamAccess, ("$project", projectId), ("$team", teamId)) is not null;
    public void SaveRepository(Repository value) => Execute("""
        INSERT INTO core_repositories(id,project_id,name,slug,default_branch,status,created_at,updated_at,archived_at)
        VALUES($id,$project,$name,$slug,$branch,$status,$created,$updated,$archived)
        ON CONFLICT(id) DO UPDATE SET name=excluded.name,slug=excluded.slug,default_branch=excluded.default_branch,
        status=excluded.status,updated_at=excluded.updated_at,archived_at=excluded.archived_at
        """, RepositoryValues(value));
    public Repository? FindRepository(Guid id) => QueryOne("SELECT * FROM core_repositories WHERE id=$value", MapRepository, ("$value", id));
    public IReadOnlyList<Repository> ListRepositories(Guid projectId) =>
        QueryMany("SELECT * FROM core_repositories WHERE project_id=$value AND archived_at IS NULL ORDER BY name", MapRepository, ("$value", projectId));
    public void DeleteRepository(Guid id) => Execute("DELETE FROM core_repositories WHERE id=$value", ("$value", id));
    public void SaveRepositoryConnection(RepositoryConnection value) => Execute("""
        INSERT INTO core_repository_connections(repository_id,provider_type,external_repository_id,external_owner,external_name,clone_url,web_url,last_synced_at,status)
        VALUES($repository,$provider,$externalId,$owner,$name,$clone,$web,$synced,$status)
        ON CONFLICT(repository_id) DO UPDATE SET provider_type=excluded.provider_type,external_repository_id=excluded.external_repository_id,
        external_owner=excluded.external_owner,external_name=excluded.external_name,clone_url=excluded.clone_url,web_url=excluded.web_url,
        last_synced_at=excluded.last_synced_at,status=excluded.status
        """, ("$repository", value.RepositoryId), ("$provider", value.ProviderType), ("$externalId", value.ExternalRepositoryId),
        ("$owner", value.ExternalOwner), ("$name", value.ExternalName), ("$clone", value.CloneUrl), ("$web", value.WebUrl),
        ("$synced", value.LastSyncedAt), ("$status", value.Status));
    public RepositoryConnection? GetRepositoryConnection(Guid repositoryId) =>
        QueryOne("SELECT * FROM core_repository_connections WHERE repository_id=$value", MapRepositoryConnection, ("$value", repositoryId));
    public void SaveWorkspace(UserRepositoryWorkspace value) => Execute("""
        INSERT INTO core_user_repository_workspaces(user_id,repository_id,local_path,last_detected_branch,last_detected_head,updated_at)
        VALUES($user,$repository,$path,$branch,$head,$updated)
        ON CONFLICT(user_id,repository_id) DO UPDATE SET local_path=excluded.local_path,last_detected_branch=excluded.last_detected_branch,
        last_detected_head=excluded.last_detected_head,updated_at=excluded.updated_at
        """, ("$user", value.UserId), ("$repository", value.RepositoryId), ("$path", value.LocalPath),
        ("$branch", value.LastDetectedBranch), ("$head", value.LastDetectedHead), ("$updated", value.UpdatedAt));
    public UserRepositoryWorkspace? GetWorkspace(Guid userId, Guid repositoryId) =>
        QueryOne("SELECT * FROM core_user_repository_workspaces WHERE user_id=$user AND repository_id=$repository", MapWorkspace,
            ("$user", userId), ("$repository", repositoryId));

    public void SaveSession(AuthSession value) => Execute("""
        INSERT INTO core_sessions(id,user_id,token_hash,created_at,expires_at,revoked_at) VALUES($id,$user,$token,$created,$expires,$revoked)
        ON CONFLICT(id) DO UPDATE SET revoked_at=excluded.revoked_at
        """, ("$id", value.Id), ("$user", value.UserId), ("$token", value.TokenHash), ("$created", value.CreatedAt),
        ("$expires", value.ExpiresAt), ("$revoked", value.RevokedAt));
    public AuthSession? FindSessionByTokenHash(string tokenHash) =>
        QueryOne("SELECT * FROM core_sessions WHERE token_hash=$value", MapSession, ("$value", tokenHash));
    public void RevokeSession(string tokenHash, DateTimeOffset revokedAt) =>
        Execute("UPDATE core_sessions SET revoked_at=$revoked WHERE token_hash=$token", ("$revoked", revokedAt), ("$token", tokenHash));

    public void SaveBootstrapSession(BootstrapSession value) => Execute("""
        INSERT INTO core_bootstrap_sessions(id,token_hash,created_at,expires_at,revoked_at)
        VALUES($id,$token,$created,$expires,$revoked)
        ON CONFLICT(id) DO UPDATE SET revoked_at=excluded.revoked_at
        """, ("$id", value.Id), ("$token", value.TokenHash), ("$created", value.CreatedAt),
        ("$expires", value.ExpiresAt), ("$revoked", value.RevokedAt));

    public BootstrapSession? FindBootstrapSession(string tokenHash) =>
        QueryOne("SELECT id,token_hash,created_at,expires_at,revoked_at FROM core_bootstrap_sessions WHERE token_hash=$value", MapBootstrapSession, ("$value", tokenHash));

    public void RevokeBootstrapSession(string tokenHash, DateTimeOffset revokedAt) =>
        Execute("UPDATE core_bootstrap_sessions SET revoked_at=$revoked WHERE token_hash=$token", ("$revoked", revokedAt), ("$token", tokenHash));

    public void RevokeAllBootstrapSessions(DateTimeOffset revokedAt) =>
        Execute("UPDATE core_bootstrap_sessions SET revoked_at=$revoked WHERE revoked_at IS NULL", ("$revoked", revokedAt));

    public LicenceRecord? GetActiveLicence() =>
        QueryOne("SELECT * FROM core_licences WHERE status='Active' ORDER BY installed_at DESC LIMIT 1", MapLicence);

    public IReadOnlyList<LicenceRecord> ListLicences() =>
        QueryMany("SELECT * FROM core_licences ORDER BY installed_at DESC", MapLicence);

    public void SaveLicence(LicenceRecord record) => Execute("""
        INSERT INTO core_licences(id,licence_id,customer_id,mode,status,issued_at,expires_at,payload,signature,installed_at)
        VALUES($id,$licenceId,$customerId,$mode,$status,$issued,$expires,$payload,$signature,$installed)
        ON CONFLICT(id) DO UPDATE SET licence_id=excluded.licence_id,customer_id=excluded.customer_id,mode=excluded.mode,
        status=excluded.status,issued_at=excluded.issued_at,expires_at=excluded.expires_at,payload=excluded.payload,
        signature=excluded.signature
        """, LicenceValues(record));

    public void ReplaceActiveLicence(LicenceRecord record)
    {
        using var connection = _connections.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "UPDATE core_licences SET status='Replaced' WHERE status='Active'", transaction);
        Execute(connection, """
            INSERT INTO core_licences(id,licence_id,customer_id,mode,status,issued_at,expires_at,payload,signature,installed_at)
            VALUES($id,$licenceId,$customerId,$mode,$status,$issued,$expires,$payload,$signature,$installed)
            """, transaction, LicenceValues(record));
        transaction.Commit();
    }

    public void MarkActiveLicence(LicenceRecordStatus status) =>
        Execute("UPDATE core_licences SET status=$status WHERE status='Active'", ("$status", status));

    public void AddLicenceHistory(LicenceHistoryEntry entry) => Execute("""
        INSERT INTO core_licence_history(id,action,mode,licence_id,detail,at)
        VALUES($id,$action,$mode,$licenceId,$detail,$at)
        """, ("$id", entry.Id), ("$action", entry.Action), ("$mode", entry.Mode),
        ("$licenceId", entry.LicenceId), ("$detail", entry.Detail), ("$at", entry.At));

    public IReadOnlyList<LicenceHistoryEntry> ListLicenceHistory() =>
        QueryMany("SELECT id,action,mode,licence_id,detail,at FROM core_licence_history ORDER BY at DESC", MapLicenceHistory);

    private const string InsertOrganisation = "INSERT INTO core_organisation(id,name,description,avatar_url,created_at,updated_at) VALUES($id,$name,$description,$avatar,$created,$updated)";
    private const string UpsertOrganisation = InsertOrganisation + " ON CONFLICT(id) DO UPDATE SET name=excluded.name,description=excluded.description,avatar_url=excluded.avatar_url,updated_at=excluded.updated_at";
    private const string InsertUser = "INSERT INTO core_users(id,email,username,password_hash,status,created_at,updated_at,last_login_at,onboarding_dismissed_at) VALUES($id,$email,$username,$password,$status,$created,$updated,$login,$dismissed)";
    private const string UpsertUser = InsertUser + " ON CONFLICT(id) DO UPDATE SET email=excluded.email,username=excluded.username,password_hash=excluded.password_hash,status=excluded.status,updated_at=excluded.updated_at,last_login_at=excluded.last_login_at,onboarding_dismissed_at=excluded.onboarding_dismissed_at";
    private const string InsertProfile = "INSERT INTO core_user_profiles(user_id,display_name,avatar_url,bio,job_title,timezone,locale,default_project_id) VALUES($user,$display,$avatar,$bio,$job,$timezone,$locale,$project)";
    private const string UpsertProfile = InsertProfile + " ON CONFLICT(user_id) DO UPDATE SET display_name=excluded.display_name,avatar_url=excluded.avatar_url,bio=excluded.bio,job_title=excluded.job_title,timezone=excluded.timezone,locale=excluded.locale,default_project_id=excluded.default_project_id";
    private const string InsertMembership = "INSERT INTO core_memberships(id,user_id,role,status,joined_at,invited_by_user_id) VALUES($id,$user,$role,$status,$joined,$inviter)";
    private const string UpsertMembership = InsertMembership + " ON CONFLICT(user_id) DO UPDATE SET role=excluded.role,status=excluded.status";

    private void Execute(string sql, params (string, object?)[] values)
    {
        using var connection = _connections.Open();
        Execute(connection, sql, null, values);
    }

    private static void Execute(DbConnection connection, string sql, DbTransaction? transaction, params (string, object?)[] values)
    {
        using var command = Command(connection, sql, transaction, values);
        command.ExecuteNonQuery();
    }

    private T? QueryOne<T>(string sql, Func<DbDataReader, T> map, params (string, object?)[] values) where T : class
    {
        using var connection = _connections.Open();
        using var command = Command(connection, sql, null, values);
        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    private IReadOnlyList<T> QueryMany<T>(string sql, Func<DbDataReader, T> map, params (string, object?)[] values)
    {
        using var connection = _connections.Open();
        using var command = Command(connection, sql, null, values);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(map(reader));
        return result;
    }

    private static DbCommand Command(DbConnection connection, string sql, DbTransaction? transaction = null, params (string, object?)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in values)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value switch
            {
                null => DBNull.Value,
                Guid guid => guid.ToString(),
                DateTimeOffset date => date.ToString("O"),
                Enum item => item.ToString(),
                _ => value
            };
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static (string, object?)[] OrganisationValues(Organisation value) =>
        [("$id", value.Id), ("$name", value.Name), ("$description", value.Description), ("$avatar", value.AvatarUrl), ("$created", value.CreatedAt), ("$updated", value.UpdatedAt)];
    private static (string, object?)[] UserValues(UserAccount value) =>
        [("$id", value.Id), ("$email", value.Email), ("$username", value.Username), ("$password", value.PasswordHash), ("$status", value.Status), ("$created", value.CreatedAt), ("$updated", value.UpdatedAt), ("$login", value.LastLoginAt), ("$dismissed", value.OnboardingDismissedAt)];
    private static (string, object?)[] ProfileValues(UserProfile value) =>
        [("$user", value.UserId), ("$display", value.DisplayName), ("$avatar", value.AvatarUrl), ("$bio", value.Bio), ("$job", value.JobTitle), ("$timezone", value.Timezone), ("$locale", value.Locale), ("$project", value.DefaultProjectId)];
    private static (string, object?)[] MembershipValues(OrganisationMembership value) =>
        [("$id", value.Id), ("$user", value.UserId), ("$role", value.Role), ("$status", value.Status), ("$joined", value.JoinedAt), ("$inviter", value.InvitedByUserId)];
    private static (string, object?)[] ProjectValues(Project value) =>
        [("$id", value.Id), ("$name", value.Name), ("$slug", value.Slug), ("$key", value.Key), ("$description", value.Description), ("$visibility", value.Visibility), ("$creator", value.CreatedByUserId), ("$created", value.CreatedAt), ("$updated", value.UpdatedAt), ("$archived", value.ArchivedAt), ("$repoMode", value.RepositoryMode)];
    private static (string, object?)[] RepositoryValues(Repository value) =>
        [("$id", value.Id), ("$project", value.ProjectId), ("$name", value.Name), ("$slug", value.Slug), ("$branch", value.DefaultBranch), ("$status", value.Status), ("$created", value.CreatedAt), ("$updated", value.UpdatedAt), ("$archived", value.ArchivedAt)];
    private static (string, object?)[] LicenceValues(LicenceRecord value) =>
        [("$id", value.Id), ("$licenceId", value.LicenceId), ("$customerId", value.CustomerId), ("$mode", value.Mode), ("$status", value.Status),
         ("$issued", value.IssuedAt), ("$expires", value.ExpiresAt), ("$payload", value.Payload), ("$signature", value.Signature), ("$installed", value.InstalledAt)];

    private static Organisation MapOrganisation(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Description = Text(r, 2), AvatarUrl = Text(r, 3), CreatedAt = RequiredDate(r, 4), UpdatedAt = RequiredDate(r, 5) };
    private static UserAccount MapUser(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), Email = r.GetString(1), Username = r.GetString(2), PasswordHash = r.GetString(3), Status = Enum.Parse<UserStatus>(r.GetString(4)), CreatedAt = RequiredDate(r, 5), UpdatedAt = RequiredDate(r, 6), LastLoginAt = Date(r, 7), OnboardingDismissedAt = Date(r, 8) };
    private static UserProfile MapProfile(DbDataReader r) => new() { UserId = Guid.Parse(r.GetString(0)), DisplayName = r.GetString(1), AvatarUrl = Text(r, 2), Bio = Text(r, 3), JobTitle = Text(r, 4), Timezone = Text(r, 5), Locale = Text(r, 6), DefaultProjectId = GuidValue(r, 7) };
    private static OrganisationMembership MapMembership(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), UserId = Guid.Parse(r.GetString(1)), Role = Enum.Parse<OrganisationRole>(r.GetString(2)), Status = Enum.Parse<MembershipStatus>(r.GetString(3)), JoinedAt = RequiredDate(r, 4), InvitedByUserId = GuidValue(r, 5) };
    private static Invitation MapInvitation(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), Email = r.GetString(1), Role = Enum.Parse<OrganisationRole>(r.GetString(2)), TokenHash = r.GetString(3), InvitedByUserId = Guid.Parse(r.GetString(4)), ExpiresAt = RequiredDate(r, 5), AcceptedAt = Date(r, 6), CreatedAt = RequiredDate(r, 7) };
    private static Team MapTeam(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), Name = r.GetString(1), Slug = r.GetString(2), Description = Text(r, 3), CreatedAt = RequiredDate(r, 4), UpdatedAt = RequiredDate(r, 5) };
    private static TeamMembership MapTeamMembership(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), TeamId = Guid.Parse(r.GetString(1)), UserId = Guid.Parse(r.GetString(2)), JoinedAt = RequiredDate(r, 3) };
    private static ProjectUserAccess MapProjectUserAccess(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), ProjectId = Guid.Parse(r.GetString(1)), UserId = Guid.Parse(r.GetString(2)), GrantedAt = RequiredDate(r, 3) };
    private static ProjectTeamAccess MapProjectTeamAccess(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), ProjectId = Guid.Parse(r.GetString(1)), TeamId = Guid.Parse(r.GetString(2)), GrantedAt = RequiredDate(r, 3) };
    private static Project MapProject(DbDataReader r)
    {
        var mode = RepositoryMode.SingleRepository;
        if (r.FieldCount > 10 && !r.IsDBNull(10))
            Enum.TryParse(r.GetString(10), true, out mode);
        return new()
        {
            Id = Guid.Parse(r.GetString(0)),
            Name = r.GetString(1),
            Slug = r.GetString(2),
            Key = r.GetString(3),
            Description = Text(r, 4),
            Visibility = Enum.Parse<ProjectVisibility>(r.GetString(5)),
            CreatedByUserId = Guid.Parse(r.GetString(6)),
            CreatedAt = RequiredDate(r, 7),
            UpdatedAt = RequiredDate(r, 8),
            ArchivedAt = Date(r, 9),
            RepositoryMode = mode
        };
    }
    private static Repository MapRepository(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), ProjectId = Guid.Parse(r.GetString(1)), Name = r.GetString(2), Slug = r.GetString(3), DefaultBranch = Text(r, 4), Status = Enum.Parse<RepositoryStatus>(r.GetString(5)), CreatedAt = RequiredDate(r, 6), UpdatedAt = RequiredDate(r, 7), ArchivedAt = Date(r, 8) };
    private static RepositoryConnection MapRepositoryConnection(DbDataReader r) => new() { RepositoryId = Guid.Parse(r.GetString(0)), ProviderType = r.GetString(1), ExternalRepositoryId = Text(r, 2), ExternalOwner = r.GetString(3), ExternalName = r.GetString(4), CloneUrl = r.GetString(5), WebUrl = Text(r, 6), LastSyncedAt = Date(r, 7), Status = Enum.Parse<RepositoryStatus>(r.GetString(8)) };
    private static UserRepositoryWorkspace MapWorkspace(DbDataReader r) => new() { UserId = Guid.Parse(r.GetString(0)), RepositoryId = Guid.Parse(r.GetString(1)), LocalPath = r.GetString(2), LastDetectedBranch = Text(r, 3), LastDetectedHead = Text(r, 4), UpdatedAt = RequiredDate(r, 5) };
    private static AuthSession MapSession(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), UserId = Guid.Parse(r.GetString(1)), TokenHash = r.GetString(2), CreatedAt = RequiredDate(r, 3), ExpiresAt = RequiredDate(r, 4), RevokedAt = Date(r, 5) };
    private static BootstrapSession MapBootstrapSession(DbDataReader r) => new() { Id = Guid.Parse(r.GetString(0)), TokenHash = r.GetString(1), CreatedAt = RequiredDate(r, 2), ExpiresAt = RequiredDate(r, 3), RevokedAt = Date(r, 4) };
    private static LicenceRecord MapLicence(DbDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        LicenceId = Text(r, 1),
        CustomerId = Text(r, 2),
        Mode = Enum.Parse<LicenceMode>(r.GetString(3)),
        Status = Enum.Parse<LicenceRecordStatus>(r.GetString(4)),
        IssuedAt = Date(r, 5),
        ExpiresAt = Date(r, 6),
        Payload = Text(r, 7),
        Signature = Text(r, 8),
        InstalledAt = RequiredDate(r, 9)
    };
    private static LicenceHistoryEntry MapLicenceHistory(DbDataReader r)
    {
        LicenceMode? mode = null;
        if (!r.IsDBNull(2) && Enum.TryParse<LicenceMode>(r.GetString(2), true, out var parsed)) mode = parsed;
        return new()
        {
            Id = Guid.Parse(r.GetString(0)),
            Action = r.GetString(1),
            Mode = mode,
            LicenceId = Text(r, 3),
            Detail = Text(r, 4),
            At = RequiredDate(r, 5)
        };
    }
    private static string? Text(DbDataReader r, int index) => r.IsDBNull(index) ? null : r.GetString(index);
    private static DateTimeOffset RequiredDate(DbDataReader r, int index) => DateTimeOffset.Parse(r.GetString(index));
    private static DateTimeOffset? Date(DbDataReader r, int index) => r.IsDBNull(index) ? null : RequiredDate(r, index);
    private static Guid? GuidValue(DbDataReader r, int index) => r.IsDBNull(index) ? null : Guid.Parse(r.GetString(index));
}
