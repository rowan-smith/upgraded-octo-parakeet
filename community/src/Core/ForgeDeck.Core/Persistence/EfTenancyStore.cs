using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.Persistence;

public sealed class EfTenancyStore : ITenancyStore
{
    private readonly IDbContextFactory<PlatformDbContext> _factory;

    public EfTenancyStore(IDbContextFactory<PlatformDbContext> factory)
    {
        _factory = factory;
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public InstanceConfiguration GetInstance()
    {
        using var db = _factory.CreateDbContext();
        var row = db.Instances.AsNoTracking().FirstOrDefault(x => x.Singleton == 1);
        if (row is null)
        {
            return new InstanceConfiguration();
        }

        return new InstanceConfiguration
        {
            State = row.State,
            InitialisedAt = row.InitialisedAt,
            InstanceId = row.InstanceId ?? Guid.Empty,
            LicenceMode = row.LicenceMode,
            BootstrapEnabled = row.BootstrapEnabled,
            SetupCompletedAt = row.SetupCompletedAt,
            ModulesAcknowledgedAt = row.ModulesAcknowledgedAt
        };
    }

    public void EnsureInstanceId()
    {
        var instance = GetInstance();
        if (instance.InstanceId != Guid.Empty)
        {
            return;
        }

        instance.InstanceId = Guid.NewGuid();
        SaveInstance(instance);
    }

    public void SaveInstance(InstanceConfiguration configuration)
    {
        using var db = _factory.CreateDbContext();
        var row = db.Instances.Find(1);
        if (row is null)
        {
            row = new InstanceRow { Singleton = 1 };
            db.Instances.Add(row);
        }

        row.State = configuration.State;
        row.InitialisedAt = configuration.InitialisedAt;
        row.InstanceId = configuration.InstanceId == Guid.Empty ? null : configuration.InstanceId;
        row.LicenceMode = configuration.LicenceMode;
        row.BootstrapEnabled = configuration.BootstrapEnabled;
        row.SetupCompletedAt = configuration.SetupCompletedAt;
        row.ModulesAcknowledgedAt = configuration.ModulesAcknowledgedAt;
        db.SaveChanges();
    }

    public Organisation? GetOrganisation()
    {
        using var db = _factory.CreateDbContext();
        return db.Organisations.AsNoTracking().FirstOrDefault();
    }

    public void Bootstrap(Organisation organisation, UserAccount user, UserProfile profile, OrganisationMembership membership)
    {
        using var db = _factory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var instance = db.Instances.Find(1)
            ?? throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        if (instance.State != InstanceState.Uninitialised)
        {
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        }

        var instanceId = Guid.NewGuid();
        db.Entry(organisation).Property("Singleton").CurrentValue = 1;
        db.Organisations.Add(organisation);
        db.Users.Add(user);
        db.Profiles.Add(profile);
        db.Memberships.Add(membership);
        db.SaveChanges();

        var now = DateTimeOffset.UtcNow;
        var affected = db.Instances
            .Where(x => x.Singleton == 1 && x.State == InstanceState.Uninitialised)
            .ExecuteUpdate(s => s
                .SetProperty(x => x.State, InstanceState.Initialised)
                .SetProperty(x => x.InitialisedAt, now)
                .SetProperty(x => x.InstanceId, x => x.InstanceId ?? instanceId)
                .SetProperty(x => x.LicenceMode, x => x.LicenceMode == LicenceMode.None ? LicenceMode.Community : x.LicenceMode)
                .SetProperty(x => x.BootstrapEnabled, false));

        if (affected != 1)
        {
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        }

        transaction.Commit();
    }

    public void CreateOwner(UserAccount user, UserProfile profile, OrganisationMembership membership)
    {
        using var db = _factory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var instance = db.Instances.Find(1)
            ?? throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        if (instance.State != InstanceState.Uninitialised)
        {
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        }

        if (!db.Organisations.Any())
        {
            throw new InvalidOperationException("Organisation must be configured before creating an owner.");
        }

        db.Users.Add(user);
        db.Profiles.Add(profile);
        db.Memberships.Add(membership);
        db.SaveChanges();

        var now = DateTimeOffset.UtcNow;
        var affected = db.Instances
            .Where(x => x.Singleton == 1 && x.State == InstanceState.Uninitialised)
            .ExecuteUpdate(s => s
                .SetProperty(x => x.State, InstanceState.Initialised)
                .SetProperty(x => x.InitialisedAt, now)
                .SetProperty(x => x.BootstrapEnabled, false));

        if (affected != 1)
        {
            throw new InvalidOperationException("This ForgeDeck instance has already been initialised.");
        }

        db.BootstrapSessions
            .Where(x => x.RevokedAt == null)
            .ExecuteUpdate(s => s.SetProperty(x => x.RevokedAt, now));

        transaction.Commit();
    }

    public void SaveOrganisation(Organisation value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Organisations.FirstOrDefault();
        if (existing is not null && existing.Id != value.Id)
        {
            throw new InvalidOperationException("A ForgeDeck installation can contain only one organisation.");
        }

        if (existing is null)
        {
            db.Entry(value).Property("Singleton").CurrentValue = 1;
            db.Organisations.Add(value);
        }
        else
        {
            existing.Name = value.Name;
            existing.Description = value.Description;
            existing.AvatarUrl = value.AvatarUrl;
            existing.UpdatedAt = value.UpdatedAt;
        }

        db.SaveChanges();
    }

    public UserAccount? FindUser(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public UserAccount? FindUserByEmail(string email)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking()
            .FirstOrDefault(x => EF.Functions.Collate(x.Email, "NOCASE") == email);
    }

    public UserAccount? FindUserByUsername(string username)
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking()
            .FirstOrDefault(x => EF.Functions.Collate(x.Username, "NOCASE") == username);
    }

    public IReadOnlyList<UserAccount> ListUsers()
    {
        using var db = _factory.CreateDbContext();
        return db.Users.AsNoTracking().OrderBy(x => x.Email).ToList();
    }

    public void SaveUser(UserAccount value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Users.Find(value.Id);
        if (existing is null)
        {
            db.Users.Add(value);
        }
        else
        {
            existing.Email = value.Email;
            existing.Username = value.Username;
            existing.PasswordHash = value.PasswordHash;
            existing.Status = value.Status;
            existing.UpdatedAt = value.UpdatedAt;
            existing.LastLoginAt = value.LastLoginAt;
            existing.OnboardingDismissedAt = value.OnboardingDismissedAt;
        }

        db.SaveChanges();
    }

    public UserProfile? GetProfile(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.Profiles.AsNoTracking().FirstOrDefault(x => x.UserId == userId);
    }

    public void SaveProfile(UserProfile value)
    {
        using var db = _factory.CreateDbContext();
        var theme = string.IsNullOrWhiteSpace(value.Theme) ? "system" : value.Theme.Trim().ToLowerInvariant();
        var existing = db.Profiles.Find(value.UserId);
        if (existing is null)
        {
            value.Theme = theme;
            db.Profiles.Add(value);
        }
        else
        {
            existing.DisplayName = value.DisplayName;
            existing.AvatarUrl = value.AvatarUrl;
            existing.Bio = value.Bio;
            existing.JobTitle = value.JobTitle;
            existing.Timezone = value.Timezone;
            existing.Locale = value.Locale;
            existing.DefaultProjectId = value.DefaultProjectId;
            existing.Theme = theme;
        }

        db.SaveChanges();
    }

    public OrganisationMembership? GetMembership(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.Memberships.AsNoTracking().FirstOrDefault(x => x.UserId == userId);
    }

    public IReadOnlyList<OrganisationMembership> ListMemberships()
    {
        using var db = _factory.CreateDbContext();
        return db.Memberships.AsNoTracking().OrderBy(x => x.JoinedAt).ToList();
    }

    public void SaveMembership(OrganisationMembership value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Memberships.FirstOrDefault(x => x.UserId == value.UserId);
        if (existing is null)
        {
            db.Memberships.Add(value);
        }
        else
        {
            existing.Role = value.Role;
            existing.Status = value.Status;
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.CreatedByUserId).CurrentValue = value.CreatedByUserId;
        }

        db.SaveChanges();
    }

    public void DeleteMembership(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.Memberships.Where(x => x.UserId == userId).ExecuteDelete();
    }

    public void SaveInvitation(Invitation value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Invitations.Find(value.Id);
        if (existing is null)
        {
            db.Invitations.Add(value);
        }
        else
        {
            existing.Email = value.Email;
            existing.Role = value.Role;
            existing.AcceptedAt = value.AcceptedAt;
        }

        db.SaveChanges();
    }

    public Invitation? FindInvitationByTokenHash(string tokenHash)
    {
        using var db = _factory.CreateDbContext();
        return db.Invitations.AsNoTracking().FirstOrDefault(x => x.TokenHash == tokenHash);
    }

    public Invitation? FindInvitation(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Invitations.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public IReadOnlyList<Invitation> ListInvitations()
    {
        using var db = _factory.CreateDbContext();
        return db.Invitations.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToList();
    }

    public void SaveAccessRole(AccessRole value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.AccessRoles.Find(value.Id);
        if (existing is null)
        {
            db.AccessRoles.Add(value);
        }
        else
        {
            existing.Name = value.Name;
            existing.Slug = value.Slug;
            existing.Description = value.Description;
            existing.Permissions = value.Permissions;
            existing.UpdatedAt = value.UpdatedAt;
            existing.ScopeType = value.ScopeType;
            existing.OwnerType = value.OwnerType;
            existing.OwnerId = value.OwnerId;
            existing.DefaultProjectRoleId = value.DefaultProjectRoleId;
        }

        db.SaveChanges();
    }

    public AccessRole? FindAccessRole(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.AccessRoles.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public AccessRole? FindAccessRoleBySlug(string slug)
    {
        using var db = _factory.CreateDbContext();
        return db.AccessRoles.AsNoTracking()
            .FirstOrDefault(x => EF.Functions.Collate(x.Slug, "NOCASE") == slug);
    }

    public IReadOnlyList<AccessRole> ListAccessRoles()
    {
        using var db = _factory.CreateDbContext();
        return db.AccessRoles.AsNoTracking()
            .OrderByDescending(x => x.IsSystem)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<AccessRole> ListAccessRoles(ScopeType scopeType, Guid? ownerId = null)
    {
        using var db = _factory.CreateDbContext();
        var query = db.AccessRoles.AsNoTracking().Where(x => x.ScopeType == scopeType);
        query = ownerId is Guid id
            ? query.Where(x => x.OwnerId == id)
            : query.Where(x => x.OwnerId == null);
        return query.OrderByDescending(x => x.IsSystem).ThenBy(x => x.Name).ToList();
    }

    public void DeleteAccessRole(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.Teams.Where(x => x.RoleId == id).ExecuteUpdate(s => s.SetProperty(x => x.RoleId, (Guid?)null));
        db.ProjectUserAccess.Where(x => x.RoleId == id).ExecuteUpdate(s => s.SetProperty(x => x.RoleId, (Guid?)null));
        db.ProjectTeamAccess.Where(x => x.RoleId == id).ExecuteUpdate(s => s.SetProperty(x => x.RoleId, (Guid?)null));
        db.AccessRoles.Where(x => x.DefaultProjectRoleId == id)
            .ExecuteUpdate(s => s.SetProperty(x => x.DefaultProjectRoleId, (Guid?)null));
        db.RoleAssignments.Where(x => x.RoleId == id).ExecuteDelete();
        db.AccessRoles.Where(x => x.Id == id).ExecuteDelete();
    }

    public void SaveRoleAssignment(RoleAssignment value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.RoleAssignments.Find(value.Id);
        if (existing is null)
        {
            db.RoleAssignments.Add(value);
        }
        else
        {
            db.Entry(existing).Property(x => x.RoleId).CurrentValue = value.RoleId;
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.AssignedByUserId).CurrentValue = value.AssignedByUserId;
        }

        db.SaveChanges();
    }

    public void DeleteRoleAssignment(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.RoleAssignments.Where(x => x.Id == id).ExecuteDelete();
    }

    public void DeleteRoleAssignmentsForRole(Guid roleId)
    {
        using var db = _factory.CreateDbContext();
        db.RoleAssignments.Where(x => x.RoleId == roleId).ExecuteDelete();
    }

    public IReadOnlyList<RoleAssignment> ListRoleAssignments(Guid userId, ScopeType? scopeType = null, Guid? scopeId = null)
    {
        using var db = _factory.CreateDbContext();
        var query = db.RoleAssignments.AsNoTracking().Where(x => x.UserId == userId);
        if (scopeType is ScopeType type && scopeId is Guid id)
        {
            query = query.Where(x => x.ScopeType == type && x.ScopeId == id);
        }
        else if (scopeType is ScopeType onlyType)
        {
            query = query.Where(x => x.ScopeType == onlyType);
        }

        return query.ToList();
    }

    public IReadOnlyList<RoleAssignment> ListRoleAssignmentsForScope(ScopeType scopeType, Guid scopeId)
    {
        using var db = _factory.CreateDbContext();
        return db.RoleAssignments.AsNoTracking()
            .Where(x => x.ScopeType == scopeType && x.ScopeId == scopeId)
            .ToList();
    }

    public void SavePermissionGrant(PermissionGrant value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.PermissionGrants.Find(value.Id);
        if (existing is null)
        {
            db.PermissionGrants.Add(value);
        }
        else
        {
            existing.PermissionId = value.PermissionId;
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.GrantedByUserId).CurrentValue = value.GrantedByUserId;
        }

        db.SaveChanges();
    }

    public void DeletePermissionGrant(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.PermissionGrants.Where(x => x.Id == id).ExecuteDelete();
    }

    public IReadOnlyList<PermissionGrant> ListPermissionGrantsForUser(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.PermissionGrants.AsNoTracking().Where(x => x.UserId == userId).ToList();
    }

    public IReadOnlyList<PermissionGrant> ListPermissionGrants(ScopeType scopeType, Guid? scopeId = null)
    {
        using var db = _factory.CreateDbContext();
        var query = db.PermissionGrants.AsNoTracking().Where(x => x.ScopeType == scopeType);
        query = scopeId is Guid id
            ? query.Where(x => x.ScopeId == id)
            : query.Where(x => x.ScopeId == null);
        return query.ToList();
    }

    public void SaveTeam(Team value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Teams.Find(value.Id);
        if (existing is null)
        {
            db.Teams.Add(value);
        }
        else
        {
            existing.Name = value.Name;
            existing.Slug = value.Slug;
            existing.Description = value.Description;
            existing.UpdatedAt = value.UpdatedAt;
            existing.RoleId = value.RoleId;
        }

        db.SaveChanges();
    }

    public Team? FindTeam(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Teams.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public Team? FindTeamBySlug(string slug)
    {
        using var db = _factory.CreateDbContext();
        return db.Teams.AsNoTracking()
            .FirstOrDefault(x => EF.Functions.Collate(x.Slug, "NOCASE") == slug);
    }

    public IReadOnlyList<Team> ListTeams()
    {
        using var db = _factory.CreateDbContext();
        return db.Teams.AsNoTracking().OrderBy(x => x.Name).ToList();
    }

    public void DeleteTeam(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.Projects.Where(x => x.OwningTeamId == id)
            .ExecuteUpdate(s => s.SetProperty(x => x.OwningTeamId, (Guid?)null));
        db.RoleAssignments.Where(x => x.ScopeType == ScopeType.Team && x.ScopeId == id).ExecuteDelete();
        db.PermissionGrants.Where(x => x.ScopeType == ScopeType.Team && x.ScopeId == id).ExecuteDelete();
        db.TeamMemberships.Where(x => x.TeamId == id).ExecuteDelete();
        db.ProjectTeamAccess.Where(x => x.TeamId == id).ExecuteDelete();
        db.AccessRoles.Where(x => x.OwnerType == RoleOwnerType.Team && x.OwnerId == id).ExecuteDelete();
        db.Teams.Where(x => x.Id == id).ExecuteDelete();
    }

    public void SaveTeamMembership(TeamMembership value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.TeamMemberships.FirstOrDefault(x => x.TeamId == value.TeamId && x.UserId == value.UserId);
        if (existing is null)
        {
            db.TeamMemberships.Add(value);
        }
        else
        {
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.CreatedByUserId).CurrentValue = value.CreatedByUserId;
        }

        db.SaveChanges();
    }

    public void DeleteTeamMembership(Guid teamId, Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.RoleAssignments
            .Where(x => x.UserId == userId && x.ScopeType == ScopeType.Team && x.ScopeId == teamId)
            .ExecuteDelete();
        db.TeamMemberships.Where(x => x.TeamId == teamId && x.UserId == userId).ExecuteDelete();
    }

    public IReadOnlyList<TeamMembership> ListTeamMemberships(Guid teamId)
    {
        using var db = _factory.CreateDbContext();
        return db.TeamMemberships.AsNoTracking().Where(x => x.TeamId == teamId).ToList();
    }

    public IReadOnlyList<TeamMembership> ListUserTeamMemberships(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.TeamMemberships.AsNoTracking().Where(x => x.UserId == userId).ToList();
    }

    public void SaveProject(Project value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Projects.Find(value.Id);
        if (existing is null)
        {
            db.Projects.Add(value);
        }
        else
        {
            existing.Name = value.Name;
            existing.Slug = value.Slug;
            existing.Key = value.Key;
            existing.Description = value.Description;
            existing.Visibility = value.Visibility;
            existing.UpdatedAt = value.UpdatedAt;
            existing.ArchivedAt = value.ArchivedAt;
            existing.RepositoryMode = value.RepositoryMode;
            existing.OwningTeamId = value.OwningTeamId;
        }

        db.SaveChanges();
    }

    public Project? FindProject(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Projects.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public Project? FindProjectBySlug(string slug)
    {
        using var db = _factory.CreateDbContext();
        return db.Projects.AsNoTracking()
            .FirstOrDefault(x => EF.Functions.Collate(x.Slug, "NOCASE") == slug);
    }

    public IReadOnlyList<Project> ListProjects()
    {
        using var db = _factory.CreateDbContext();
        return db.Projects.AsNoTracking()
            .Where(x => x.ArchivedAt == null)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<Guid> ListStarredProjectIds(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectStars.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StarredAt)
            .Select(x => x.ProjectId)
            .ToList();
    }

    public bool IsProjectStarred(Guid userId, Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectStars.AsNoTracking()
            .Any(x => x.UserId == userId && x.ProjectId == projectId);
    }

    public void StarProject(Guid userId, Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        if (db.ProjectStars.Any(x => x.UserId == userId && x.ProjectId == projectId))
        {
            return;
        }

        db.ProjectStars.Add(new UserProjectStar
        {
            UserId = userId,
            ProjectId = projectId,
            StarredAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    public void UnstarProject(Guid userId, Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        db.ProjectStars.Where(x => x.UserId == userId && x.ProjectId == projectId).ExecuteDelete();
    }

    public void DeleteProject(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.PermissionGrants.Where(x => x.ScopeType == ScopeType.Project && x.ScopeId == id).ExecuteDelete();
        db.RoleAssignments.Where(x => x.ScopeType == ScopeType.Project && x.ScopeId == id).ExecuteDelete();
        db.Projects.Where(x => x.Id == id).ExecuteDelete();
    }

    public void SaveProjectUserAccess(ProjectUserAccess value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.ProjectUserAccess.FirstOrDefault(x => x.ProjectId == value.ProjectId && x.UserId == value.UserId);
        if (existing is null)
        {
            db.ProjectUserAccess.Add(value);
        }
        else
        {
            existing.RoleId = value.RoleId;
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.GrantedByUserId).CurrentValue = value.GrantedByUserId;
        }

        db.SaveChanges();
    }

    public void DeleteProjectUserAccess(Guid projectId, Guid userId)
    {
        using var db = _factory.CreateDbContext();
        db.ProjectUserAccess.Where(x => x.ProjectId == projectId && x.UserId == userId).ExecuteDelete();
    }

    public IReadOnlyList<ProjectUserAccess> ListProjectUserAccess(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectUserAccess.AsNoTracking().Where(x => x.ProjectId == projectId).ToList();
    }

    public IReadOnlyList<ProjectUserAccess> ListUserProjectAccess(Guid userId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectUserAccess.AsNoTracking().Where(x => x.UserId == userId).ToList();
    }

    public bool HasProjectUserAccess(Guid projectId, Guid userId)
    {
        using var db = _factory.CreateDbContext();
        var grant = db.ProjectUserAccess.AsNoTracking()
            .FirstOrDefault(x => x.ProjectId == projectId && x.UserId == userId);
        return grant is not null && grant.IsEffectivelyActive();
    }

    public void SaveProjectTeamAccess(ProjectTeamAccess value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.ProjectTeamAccess.FirstOrDefault(x => x.ProjectId == value.ProjectId && x.TeamId == value.TeamId);
        if (existing is null)
        {
            db.ProjectTeamAccess.Add(value);
        }
        else
        {
            existing.RoleId = value.RoleId;
            existing.ExpiresAt = value.ExpiresAt;
            db.Entry(existing).Property(x => x.GrantedByUserId).CurrentValue = value.GrantedByUserId;
            existing.Relationship = value.Relationship;
        }

        db.SaveChanges();
    }

    public void DeleteProjectTeamAccess(Guid projectId, Guid teamId)
    {
        using var db = _factory.CreateDbContext();
        db.ProjectTeamAccess.Where(x => x.ProjectId == projectId && x.TeamId == teamId).ExecuteDelete();
    }

    public IReadOnlyList<ProjectTeamAccess> ListProjectTeamAccess(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectTeamAccess.AsNoTracking().Where(x => x.ProjectId == projectId).ToList();
    }

    public bool HasProjectTeamAccess(Guid projectId, Guid teamId)
    {
        using var db = _factory.CreateDbContext();
        var grant = db.ProjectTeamAccess.AsNoTracking()
            .FirstOrDefault(x => x.ProjectId == projectId && x.TeamId == teamId);
        return grant is not null && grant.IsEffectivelyActive();
    }

    public IReadOnlyList<ProjectModuleSetting> ListProjectModules(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.ProjectModules.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.ExtensionId)
            .ToList();
    }

    public void ReplaceProjectModules(Guid projectId, IReadOnlyList<ProjectModuleSetting> modules)
    {
        using var db = _factory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        db.ProjectModules.Where(x => x.ProjectId == projectId).ExecuteDelete();
        foreach (var module in modules)
        {
            db.ProjectModules.Add(new ProjectModuleSetting
            {
                ProjectId = projectId,
                ExtensionId = module.ExtensionId,
                Enabled = module.Enabled
            });
        }

        db.SaveChanges();
        transaction.Commit();
    }

    public bool IsProjectModuleEnabled(Guid projectId, string extensionId)
    {
        var rows = ListProjectModules(projectId);
        if (rows.Count == 0)
        {
            return true;
        }

        return rows.Any(r =>
            string.Equals(r.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase) && r.Enabled);
    }

    public void SaveRepository(Repository value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Repositories.Find(value.Id);
        if (existing is null)
        {
            db.Repositories.Add(value);
        }
        else
        {
            existing.Name = value.Name;
            existing.Slug = value.Slug;
            existing.DefaultBranch = value.DefaultBranch;
            existing.Status = value.Status;
            existing.UpdatedAt = value.UpdatedAt;
            existing.ArchivedAt = value.ArchivedAt;
        }

        db.SaveChanges();
    }

    public Repository? FindRepository(Guid id)
    {
        using var db = _factory.CreateDbContext();
        return db.Repositories.AsNoTracking().FirstOrDefault(x => x.Id == id);
    }

    public IReadOnlyList<Repository> ListRepositories(Guid projectId)
    {
        using var db = _factory.CreateDbContext();
        return db.Repositories.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ArchivedAt == null)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public void DeleteRepository(Guid id)
    {
        using var db = _factory.CreateDbContext();
        db.Repositories.Where(x => x.Id == id).ExecuteDelete();
    }

    public void SaveRepositoryConnection(RepositoryConnection value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.RepositoryConnections.Find(value.RepositoryId);
        if (existing is null)
        {
            db.RepositoryConnections.Add(value);
        }
        else
        {
            existing.ProviderType = value.ProviderType;
            existing.ExternalRepositoryId = value.ExternalRepositoryId;
            existing.ExternalOwner = value.ExternalOwner;
            existing.ExternalName = value.ExternalName;
            existing.CloneUrl = value.CloneUrl;
            existing.WebUrl = value.WebUrl;
            existing.LastSyncedAt = value.LastSyncedAt;
            existing.Status = value.Status;
        }

        db.SaveChanges();
    }

    public RepositoryConnection? GetRepositoryConnection(Guid repositoryId)
    {
        using var db = _factory.CreateDbContext();
        return db.RepositoryConnections.AsNoTracking().FirstOrDefault(x => x.RepositoryId == repositoryId);
    }

    public void SaveWorkspace(UserRepositoryWorkspace value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Workspaces.Find(value.UserId, value.RepositoryId);
        if (existing is null)
        {
            db.Workspaces.Add(value);
        }
        else
        {
            existing.LocalPath = value.LocalPath;
            existing.LastDetectedBranch = value.LastDetectedBranch;
            existing.LastDetectedHead = value.LastDetectedHead;
            existing.UpdatedAt = value.UpdatedAt;
        }

        db.SaveChanges();
    }

    public UserRepositoryWorkspace? GetWorkspace(Guid userId, Guid repositoryId)
    {
        using var db = _factory.CreateDbContext();
        return db.Workspaces.AsNoTracking()
            .FirstOrDefault(x => x.UserId == userId && x.RepositoryId == repositoryId);
    }

    public void SaveSession(AuthSession value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Sessions.Find(value.Id);
        if (existing is null)
        {
            db.Sessions.Add(value);
        }
        else
        {
            existing.RevokedAt = value.RevokedAt;
        }

        db.SaveChanges();
    }

    public AuthSession? FindSessionByTokenHash(string tokenHash)
    {
        using var db = _factory.CreateDbContext();
        return db.Sessions.AsNoTracking().FirstOrDefault(x => x.TokenHash == tokenHash);
    }

    public void RevokeSession(string tokenHash, DateTimeOffset revokedAt)
    {
        using var db = _factory.CreateDbContext();
        db.Sessions.Where(x => x.TokenHash == tokenHash)
            .ExecuteUpdate(s => s.SetProperty(x => x.RevokedAt, revokedAt));
    }

    public void SaveBootstrapSession(BootstrapSession value)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.BootstrapSessions.Find(value.Id);
        if (existing is null)
        {
            db.BootstrapSessions.Add(value);
        }
        else
        {
            existing.RevokedAt = value.RevokedAt;
        }

        db.SaveChanges();
    }

    public BootstrapSession? FindBootstrapSession(string tokenHash)
    {
        using var db = _factory.CreateDbContext();
        return db.BootstrapSessions.AsNoTracking().FirstOrDefault(x => x.TokenHash == tokenHash);
    }

    public void RevokeBootstrapSession(string tokenHash, DateTimeOffset revokedAt)
    {
        using var db = _factory.CreateDbContext();
        db.BootstrapSessions.Where(x => x.TokenHash == tokenHash)
            .ExecuteUpdate(s => s.SetProperty(x => x.RevokedAt, revokedAt));
    }

    public void RevokeAllBootstrapSessions(DateTimeOffset revokedAt)
    {
        using var db = _factory.CreateDbContext();
        db.BootstrapSessions.Where(x => x.RevokedAt == null)
            .ExecuteUpdate(s => s.SetProperty(x => x.RevokedAt, revokedAt));
    }

    public LicenceRecord? GetActiveLicence()
    {
        using var db = _factory.CreateDbContext();
        return db.Licences.AsNoTracking()
            .Where(x => x.Status == LicenceRecordStatus.Active)
            .OrderByDescending(x => x.InstalledAt)
            .FirstOrDefault();
    }

    public IReadOnlyList<LicenceRecord> ListLicences()
    {
        using var db = _factory.CreateDbContext();
        return db.Licences.AsNoTracking().OrderByDescending(x => x.InstalledAt).ToList();
    }

    public void SaveLicence(LicenceRecord record)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Licences.Find(record.Id);
        if (existing is null)
        {
            db.Licences.Add(record);
        }
        else
        {
            existing.LicenceId = record.LicenceId;
            existing.CustomerId = record.CustomerId;
            existing.Mode = record.Mode;
            existing.Status = record.Status;
            existing.IssuedAt = record.IssuedAt;
            existing.ExpiresAt = record.ExpiresAt;
            existing.Payload = record.Payload;
            existing.Signature = record.Signature;
        }

        db.SaveChanges();
    }

    public void ReplaceActiveLicence(LicenceRecord record)
    {
        using var db = _factory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        db.Licences.Where(x => x.Status == LicenceRecordStatus.Active)
            .ExecuteUpdate(s => s.SetProperty(x => x.Status, LicenceRecordStatus.Replaced));
        db.Licences.Add(record);
        db.SaveChanges();
        transaction.Commit();
    }

    public void MarkActiveLicence(LicenceRecordStatus status)
    {
        using var db = _factory.CreateDbContext();
        db.Licences.Where(x => x.Status == LicenceRecordStatus.Active)
            .ExecuteUpdate(s => s.SetProperty(x => x.Status, status));
    }

    public void AddLicenceHistory(LicenceHistoryEntry entry)
    {
        using var db = _factory.CreateDbContext();
        db.LicenceHistory.Add(entry);
        db.SaveChanges();
    }

    public IReadOnlyList<LicenceHistoryEntry> ListLicenceHistory()
    {
        using var db = _factory.CreateDbContext();
        return db.LicenceHistory.AsNoTracking().OrderByDescending(x => x.At).ToList();
    }
}
