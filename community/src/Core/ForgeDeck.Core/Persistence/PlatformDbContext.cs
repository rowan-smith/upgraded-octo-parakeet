using System.Text.Json;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace ForgeDeck.Core.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<InstanceRow> Instances => Set<InstanceRow>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<OrganisationMembership> Memberships => Set<OrganisationMembership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AccessRole> AccessRoles => Set<AccessRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectUserAccess> ProjectUserAccess => Set<ProjectUserAccess>();
    public DbSet<ProjectTeamAccess> ProjectTeamAccess => Set<ProjectTeamAccess>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<RepositoryConnection> RepositoryConnections => Set<RepositoryConnection>();
    public DbSet<UserRepositoryWorkspace> Workspaces => Set<UserRepositoryWorkspace>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();
    public DbSet<BootstrapSession> BootstrapSessions => Set<BootstrapSession>();
    public DbSet<LicenceRecord> Licences => Set<LicenceRecord>();
    public DbSet<LicenceHistoryEntry> LicenceHistory => Set<LicenceHistoryEntry>();
    public DbSet<ProviderCredentialRow> Integrations => Set<ProviderCredentialRow>();
    public DbSet<SourceConnectionRow> SourceConnections => Set<SourceConnectionRow>();
    public DbSet<LocalRepositoryRow> LocalRepositories => Set<LocalRepositoryRow>();
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();
    public DbSet<UserProjectStar> ProjectStars => Set<UserProjectStar>();
    public DbSet<ExtensionInstallation> Extensions => Set<ExtensionInstallation>();
    public DbSet<ProjectModuleSetting> ProjectModules => Set<ProjectModuleSetting>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlite(connectionString);

    /// <summary>Prefer <see cref="DatabaseProvider.Configure"/>; retained for callers that only have a connection string.</summary>
    public static void Configure(DbContextOptionsBuilder options, IConfiguration configuration, string? connectionString) =>
        DatabaseProvider.Configure(options, configuration, connectionString);

    public static void EnsureCreated(PlatformDbContext db)
    {
        db.Database.EnsureCreated();
        if (!db.Instances.Any())
        {
            db.Instances.Add(new InstanceRow { Singleton = 1 });
            db.SaveChanges();
        }

        EnsureInstanceSchema(db);
        SchemaBootstrap.Record(db, "platform", SchemaBootstrap.PlatformSchemaVersion);
    }

    /// <summary>
    /// Additive SQLite column upgrades for existing EnsureCreated databases (no EF migrations yet).
    /// </summary>
    private static void EnsureInstanceSchema(PlatformDbContext db)
    {
        TryAddInstanceColumn(db, "members_acknowledged_at");
        TryAddInstanceColumn(db, "project_acknowledged_at");
        TryAddColumn(db, "ALTER TABLE core_extensions ADD COLUMN installed_from TEXT NULL");
        TryAddColumn(db, "ALTER TABLE core_extensions ADD COLUMN package_digest TEXT NULL");

        // Legacy installs finished the old Modules→Owner wizard before optional Members/Project steps existed.
        db.Database.ExecuteSqlRaw("""
            UPDATE core_instance
            SET setup_completed_at = COALESCE(setup_completed_at, initialised_at, CURRENT_TIMESTAMP),
                members_acknowledged_at = COALESCE(members_acknowledged_at, initialised_at, CURRENT_TIMESTAMP),
                project_acknowledged_at = COALESCE(project_acknowledged_at, initialised_at, CURRENT_TIMESTAMP)
            WHERE state = 'Initialised'
              AND setup_completed_at IS NULL
              AND modules_acknowledged_at IS NOT NULL
            """);

        // Migrate legacy Build module runtime id.
        db.Database.ExecuteSqlRaw("""
            UPDATE core_extensions SET runtime_id='build' WHERE runtime_id='pipelines'
            """);
    }

    private static void TryAddInstanceColumn(PlatformDbContext db, string column)
    {
        // Columns are fixed literals from EnsureInstanceSchema callers only.
        var sql = column switch
        {
            "members_acknowledged_at" => "ALTER TABLE core_instance ADD COLUMN members_acknowledged_at TEXT NULL",
            "project_acknowledged_at" => "ALTER TABLE core_instance ADD COLUMN project_acknowledged_at TEXT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };
        TryAddColumn(db, sql);
    }

    private static void TryAddColumn(PlatformDbContext db, string sql)
    {
        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // Column already exists.
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot ORDER BY DateTimeOffset; store as ISO-8601 text so ordering works.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureInstance(modelBuilder);
        ConfigureOrganisation(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureProfiles(modelBuilder);
        ConfigureMemberships(modelBuilder);
        ConfigureInvitations(modelBuilder);
        ConfigureAccessRoles(modelBuilder);
        ConfigureTeams(modelBuilder);
        ConfigureTeamMemberships(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureProjectAccess(modelBuilder);
        ConfigureRepositories(modelBuilder);
        ConfigureSessions(modelBuilder);
        ConfigureLicences(modelBuilder);
        ConfigureIntegrations(modelBuilder);
        ConfigureSourceConnections(modelBuilder);
        ConfigureLocalRepositories(modelBuilder);
        ConfigureAudit(modelBuilder);
        ConfigureStars(modelBuilder);
        ConfigureExtensions(modelBuilder);
        ConfigureProjectModules(modelBuilder);
        ConfigureRoleAssignments(modelBuilder);
        ConfigurePermissionGrants(modelBuilder);
    }

    private static void ConfigureInstance(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstanceRow>(e =>
        {
            e.ToTable("core_instance");
            e.HasKey(x => x.Singleton);
            e.Property(x => x.Singleton).HasColumnName("singleton");
            e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
            e.Property(x => x.InitialisedAt).HasColumnName("initialised_at");
            e.Property(x => x.InstanceId).HasColumnName("instance_id");
            e.Property(x => x.LicenceMode).HasColumnName("licence_mode").HasConversion<string>().IsRequired();
            e.Property(x => x.BootstrapEnabled).HasColumnName("bootstrap_enabled").IsRequired();
            e.Property(x => x.SetupCompletedAt).HasColumnName("setup_completed_at");
            e.Property(x => x.ModulesAcknowledgedAt).HasColumnName("modules_acknowledged_at");
            e.Property(x => x.MembersAcknowledgedAt).HasColumnName("members_acknowledged_at");
            e.Property(x => x.ProjectAcknowledgedAt).HasColumnName("project_acknowledged_at");
            e.HasData(new InstanceRow
            {
                Singleton = 1,
                State = InstanceState.Uninitialised,
                LicenceMode = LicenceMode.None,
                BootstrapEnabled = true
            });
        });
    }

    private static void ConfigureOrganisation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organisation>(e =>
        {
            e.ToTable("core_organisation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property<int>("Singleton").HasColumnName("singleton").HasDefaultValue(1);
            e.HasIndex("Singleton").IsUnique();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("core_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.Username).HasColumnName("username").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.OnboardingDismissedAt).HasColumnName("onboarding_dismissed_at");
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
        });
    }

    private static void ConfigureProfiles(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(e =>
        {
            e.ToTable("core_user_profiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired();
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
            e.Property(x => x.Bio).HasColumnName("bio");
            e.Property(x => x.JobTitle).HasColumnName("job_title");
            e.Property(x => x.Timezone).HasColumnName("timezone");
            e.Property(x => x.Locale).HasColumnName("locale");
            e.Property(x => x.DefaultProjectId).HasColumnName("default_project_id");
            e.Property(x => x.Theme).HasColumnName("theme").HasDefaultValue("system");
        });
    }

    private static void ConfigureMemberships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganisationMembership>(e =>
        {
            e.ToTable("core_memberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasConversion<string>().IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            e.Property(x => x.JoinedAt).HasColumnName("joined_at").IsRequired();
            e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.HasIndex(x => x.UserId).IsUnique();
        });
    }

    private static void ConfigureInvitations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invitation>(e =>
        {
            e.ToTable("core_invitations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Email).HasColumnName("email").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.Role).HasColumnName("role").HasConversion<string>().IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
        });
    }

    private static void ConfigureAccessRoles(ModelBuilder modelBuilder)
    {
        var permissionsComparer = new ValueComparer<HashSet<string>>(
            (a, b) => a != null && b != null && a.SetEquals(b),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.OrdinalIgnoreCase.GetHashCode(item))),
            v => new HashSet<string>(v, StringComparer.OrdinalIgnoreCase));

        var permissionsConverter = new ValueConverter<HashSet<string>, string>(
            v => JsonSerializer.Serialize(v.OrderBy(p => p, StringComparer.Ordinal).ToArray(), (JsonSerializerOptions?)null),
            v => DeserializePermissions(v));

        modelBuilder.Entity<AccessRole>(e =>
        {
            e.ToTable("core_access_roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsSystem).HasColumnName("is_system").IsRequired();
            e.Property(x => x.Permissions)
                .HasColumnName("permissions_json")
                .HasConversion(permissionsConverter, permissionsComparer);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.ScopeType).HasColumnName("scope_type").HasConversion<string>().HasDefaultValue(ScopeType.Organisation);
            e.Property(x => x.OwnerType).HasColumnName("owner_type").HasConversion<string>();
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.DefaultProjectRoleId).HasColumnName("default_project_role_id");
            e.HasIndex(x => x.Slug).IsUnique();
        });
    }

    private static void ConfigureTeams(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Team>(e =>
        {
            e.ToTable("core_teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.HasIndex(x => x.Slug).IsUnique();
        });
    }

    private static void ConfigureTeamMemberships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamMembership>(e =>
        {
            e.ToTable("core_team_memberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.JoinedAt).HasColumnName("joined_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
        });
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("core_projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.Key).HasColumnName("key").IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.ArchivedAt).HasColumnName("archived_at");
            e.Property(x => x.RepositoryMode).HasColumnName("repository_mode").HasConversion<string>()
                .HasDefaultValue(RepositoryMode.SingleRepository);
            e.Property(x => x.OwningTeamId).HasColumnName("owning_team_id");
            e.HasIndex(x => x.Slug).IsUnique();
        });
    }

    private static void ConfigureProjectAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectUserAccess>(e =>
        {
            e.ToTable("core_project_user_access");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.GrantedAt).HasColumnName("granted_at").IsRequired();
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            e.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<ProjectTeamAccess>(e =>
        {
            e.ToTable("core_project_team_access");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            e.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
            e.Property(x => x.GrantedAt).HasColumnName("granted_at").IsRequired();
            e.Property(x => x.RoleId).HasColumnName("role_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            e.Property(x => x.Relationship).HasColumnName("relationship").HasConversion<string>()
                .HasDefaultValue(ProjectTeamRelationship.Access);
            e.HasIndex(x => new { x.ProjectId, x.TeamId }).IsUnique();
        });
    }

    private static void ConfigureRepositories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Repository>(e =>
        {
            e.ToTable("core_repositories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired().UseCollation("NOCASE");
            e.Property(x => x.DefaultBranch).HasColumnName("default_branch");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.ArchivedAt).HasColumnName("archived_at");
            e.HasIndex(x => new { x.ProjectId, x.Slug }).IsUnique();
        });

        modelBuilder.Entity<RepositoryConnection>(e =>
        {
            e.ToTable("core_repository_connections");
            e.HasKey(x => x.RepositoryId);
            e.Property(x => x.RepositoryId).HasColumnName("repository_id");
            e.Property(x => x.ProviderType).HasColumnName("provider_type").IsRequired();
            e.Property(x => x.ExternalRepositoryId).HasColumnName("external_repository_id");
            e.Property(x => x.ExternalOwner).HasColumnName("external_owner").IsRequired();
            e.Property(x => x.ExternalName).HasColumnName("external_name").IsRequired();
            e.Property(x => x.CloneUrl).HasColumnName("clone_url").IsRequired();
            e.Property(x => x.WebUrl).HasColumnName("web_url");
            e.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<UserRepositoryWorkspace>(e =>
        {
            e.ToTable("core_user_repository_workspaces");
            e.HasKey(x => new { x.UserId, x.RepositoryId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RepositoryId).HasColumnName("repository_id");
            e.Property(x => x.LocalPath).HasColumnName("local_path").IsRequired();
            e.Property(x => x.LastDetectedBranch).HasColumnName("last_detected_branch");
            e.Property(x => x.LastDetectedHead).HasColumnName("last_detected_head");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });
    }

    private static void ConfigureSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthSession>(e =>
        {
            e.ToTable("core_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<BootstrapSession>(e =>
        {
            e.ToTable("core_bootstrap_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(x => x.TokenHash).IsUnique();
        });
    }

    private static void ConfigureLicences(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LicenceRecord>(e =>
        {
            e.ToTable("core_licences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LicenceId).HasColumnName("licence_id");
            e.Property(x => x.CustomerId).HasColumnName("customer_id");
            e.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            e.Property(x => x.IssuedAt).HasColumnName("issued_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.Payload).HasColumnName("payload");
            e.Property(x => x.Signature).HasColumnName("signature");
            e.Property(x => x.InstalledAt).HasColumnName("installed_at").IsRequired();
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<LicenceHistoryEntry>(e =>
        {
            e.ToTable("core_licence_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Action).HasColumnName("action").IsRequired();
            e.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>();
            e.Property(x => x.LicenceId).HasColumnName("licence_id");
            e.Property(x => x.Detail).HasColumnName("detail");
            e.Property(x => x.At).HasColumnName("at").IsRequired();
        });
    }

    private static void ConfigureIntegrations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderCredentialRow>(e =>
        {
            e.ToTable("core_integrations");
            e.HasKey(x => x.ProviderId);
            e.Property(x => x.ProviderId).HasColumnName("provider_id");
            e.Property(x => x.ProtectedSecret).HasColumnName("protected_secret").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });
    }

    private static void ConfigureSourceConnections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceConnectionRow>(e =>
        {
            e.ToTable("core_source_connections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id").IsRequired();
            e.Property(x => x.ProviderId).HasColumnName("provider_id").IsRequired();
            e.Property(x => x.Owner).HasColumnName("owner").IsRequired();
            e.Property(x => x.RepositoryName).HasColumnName("repository_name").IsRequired();
            e.Property(x => x.DefaultBranch).HasColumnName("default_branch").IsRequired();
            e.Property(x => x.Url).HasColumnName("url").IsRequired();
            e.Property(x => x.ConnectedAt).HasColumnName("connected_at").IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.ProviderId, x.Owner, x.RepositoryName }).IsUnique();
        });
    }

    private static void ConfigureLocalRepositories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalRepositoryRow>(e =>
        {
            e.ToTable("core_local_repositories");
            e.HasKey(x => x.ProjectId);
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Path).HasColumnName("path").IsRequired();
            e.Property(x => x.Root).HasColumnName("root").IsRequired();
            e.Property(x => x.AssociatedAt).HasColumnName("associated_at").IsRequired();
        });
    }

    private static void ConfigureAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventRow>(e =>
        {
            e.ToTable("core_audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Actor).HasColumnName("actor").IsRequired();
            e.Property(x => x.Organisation).HasColumnName("organisation").IsRequired();
            e.Property(x => x.Project).HasColumnName("project").IsRequired();
            e.Property(x => x.Module).HasColumnName("module").IsRequired();
            e.Property(x => x.Action).HasColumnName("action").IsRequired();
            e.Property(x => x.Resource).HasColumnName("resource").IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            e.Property(x => x.MetadataJson).HasColumnName("metadata");
            e.HasIndex(x => x.Timestamp);
        });
    }

    private static void ConfigureStars(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProjectStar>(e =>
        {
            e.ToTable("core_user_project_stars");
            e.HasKey(x => new { x.UserId, x.ProjectId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.StarredAt).HasColumnName("starred_at").IsRequired();
            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigureExtensions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExtensionInstallation>(e =>
        {
            e.ToTable("core_extensions");
            e.HasKey(x => x.ExtensionId);
            e.Property(x => x.ExtensionId).HasColumnName("extension_id");
            e.Property(x => x.Type).HasColumnName("type").HasConversion<string>().IsRequired();
            e.Property(x => x.RuntimeId).HasColumnName("runtime_id");
            e.Property(x => x.InstalledVersion).HasColumnName("installed_version");
            e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
            e.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
            e.Property(x => x.InstalledAt).HasColumnName("installed_at");
            e.Property(x => x.InstalledBy).HasColumnName("installed_by");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.RestartRequired).HasColumnName("restart_required").IsRequired();
            e.Property(x => x.InstalledFrom).HasColumnName("installed_from").HasConversion<string>();
            e.Property(x => x.PackageDigest).HasColumnName("package_digest");
        });
    }

    private static void ConfigureProjectModules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectModuleSetting>(e =>
        {
            e.ToTable("core_project_modules");
            e.HasKey(x => new { x.ProjectId, x.ExtensionId });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ExtensionId).HasColumnName("extension_id").IsRequired();
            e.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        });
    }

    private static void ConfigureRoleAssignments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoleAssignment>(e =>
        {
            e.ToTable("core_role_assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
            e.Property(x => x.ScopeType).HasColumnName("scope_type").HasConversion<string>().IsRequired();
            e.Property(x => x.ScopeId).HasColumnName("scope_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
            e.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id");
            e.HasIndex(x => x.UserId);
        });
    }

    private static void ConfigurePermissionGrants(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PermissionGrant>(e =>
        {
            e.ToTable("core_permission_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.PermissionId).HasColumnName("permission_id").IsRequired();
            e.Property(x => x.ScopeType).HasColumnName("scope_type").HasConversion<string>().IsRequired();
            e.Property(x => x.ScopeId).HasColumnName("scope_id");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.GrantedAt).HasColumnName("granted_at").IsRequired();
            e.Property(x => x.GrantedByUserId).HasColumnName("granted_by_user_id");
            e.HasIndex(x => x.UserId);
        });
    }

    private static HashSet<string> DeserializePermissions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }
}
