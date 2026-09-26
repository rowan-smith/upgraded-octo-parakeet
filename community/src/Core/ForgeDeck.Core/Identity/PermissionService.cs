using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Identity;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, ResourceScope scope, CancellationToken cancellationToken = default);
    Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, ResourceScope scope, CancellationToken cancellationToken = default);
    Task<PermissionExplanation> ExplainAsync(Guid userId, string permission, ResourceScope scope, CancellationToken cancellationToken = default);
    void InvalidateUser(Guid userId);
    void InvalidateAll();
}

/// <summary>
/// Additive scoped RBAC evaluator. Effective permissions are the union of organisation role permissions,
/// organisation direct grants, team role/direct grants (team scope), and project role/direct grants
/// (project scope), after filtering inactive module permissions and expired assignments.
/// </summary>
public sealed class PermissionService(
    ITenancyStore store,
    RoleService roles,
    IPermissionDefinitionRegistry registry,
    ProjectAccessService? access = null) : IPermissionService
{
    private int _generation;

    public Task<bool> HasPermissionAsync(Guid userId, string permission, ResourceScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(userId, scope).Contains(permission));

    public Task<EffectivePermissionSet> GetEffectivePermissionsAsync(Guid userId, ResourceScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(userId, scope));

    public Task<PermissionExplanation> ExplainAsync(Guid userId, string permission, ResourceScope scope, CancellationToken cancellationToken = default)
    {
        var set = Resolve(userId, scope);
        set.Sources.TryGetValue(permission, out var sources);
        return Task.FromResult(new PermissionExplanation(
            permission,
            set.Contains(permission),
            scope,
            sources ?? Array.Empty<PermissionSource>()));
    }

    public void InvalidateUser(Guid userId) => Interlocked.Increment(ref _generation);

    public void InvalidateAll() => Interlocked.Increment(ref _generation);

    public IReadOnlySet<string> ForUser(Guid userId, Guid? projectId = null) =>
        Resolve(userId, projectId is Guid id ? ResourceScope.ForProject(id) : ResourceScope.Organisation).Permissions;

    public bool Has(Guid userId, string permission, Guid? projectId = null) =>
        ForUser(userId, projectId).Contains(permission);

    private EffectivePermissionSet Resolve(Guid userId, ResourceScope scope)
    {
        // Generation is bumped on Invalidate* so callers can reason about cache invalidation;
        // evaluation is currently uncached so permission changes take effect immediately.
        _ = Volatile.Read(ref _generation);
        return Compute(userId, scope);
    }

    private EffectivePermissionSet Compute(Guid userId, ResourceScope scope)
    {
        var membership = store.GetMembership(userId);
        if (membership is null || !membership.IsEffectivelyActive())
        {
            return EffectivePermissionSet.Empty;
        }

        // Treat expired membership as inactive without mutating storage.
        if (membership.ExpiresAt is DateTimeOffset expires && expires <= DateTimeOffset.UtcNow)
        {
            return EffectivePermissionSet.Empty;
        }

        roles.EnsureSystemRoles();
        var activeKeys = registry.ActiveKeys();
        var sources = new Dictionary<string, List<PermissionSource>>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        AddRolePermissions(
            sources,
            OrganisationPermissions.ForRole(membership.Role),
            new PermissionSource(
                Kind: "organisation_role",
                RoleSlug: SystemAccessRoles.SlugFor(membership.Role),
                RoleName: membership.Role.ToString(),
                RoleId: roles.FindBySlug(SystemAccessRoles.SlugFor(membership.Role))?.Id,
                TeamId: null,
                TeamName: null,
                ProjectId: null,
                ProjectName: null,
                GrantedByUserId: membership.InvitedByUserId,
                GrantedAt: membership.JoinedAt,
                ExpiresAt: membership.ExpiresAt),
            ScopeType.Organisation);

        foreach (var grant in store.ListPermissionGrantsForUser(userId)
                     .Where(g => !g.IsExpired(now) && g.ScopeType == ScopeType.Organisation && g.ScopeId is null))
        {
            AddPermission(sources, grant.PermissionId, new PermissionSource(
                "direct_grant", null, null, null, null, null, null, null,
                grant.GrantedByUserId, grant.GrantedAt, grant.ExpiresAt));
        }

        if (scope.Type == ScopeType.Team && scope.Id is Guid teamId)
        {
            AddTeamScoped(sources, userId, teamId, now);
        }
        else if (scope.Type == ScopeType.Project && scope.Id is Guid projectId)
        {
            AddProjectScoped(sources, userId, membership.Role, projectId, now);
        }
        else if (scope.Type == ScopeType.Organisation)
        {
            // Organisation scope also includes team-scoped admin permissions for teams the user leads.
            foreach (var teamMembership in ActiveTeamMemberships(userId, now))
            {
                AddTeamScoped(sources, userId, teamMembership.TeamId, now);
            }
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var provenance = new Dictionary<string, IReadOnlyList<PermissionSource>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, list) in sources)
        {
            if (!activeKeys.Contains(key))
            {
                continue;
            }

            permissions.Add(key);
            provenance[key] = list;
        }

        return new EffectivePermissionSet { Permissions = permissions, Sources = provenance };
    }

    private void AddTeamScoped(
        Dictionary<string, List<PermissionSource>> sources,
        Guid userId,
        Guid teamId,
        DateTimeOffset now)
    {
        var team = store.FindTeam(teamId);
        if (team is null)
        {
            return;
        }

        var membership = store.ListUserTeamMemberships(userId)
            .FirstOrDefault(m => m.TeamId == teamId && m.IsEffectivelyActive(now));
        if (membership is null)
        {
            return;
        }

        foreach (var assignment in store.ListRoleAssignments(userId, ScopeType.Team, teamId)
                     .Where(a => !a.IsExpired(now)))
        {
            var role = store.FindAccessRole(assignment.RoleId);
            if (role is null)
            {
                continue;
            }

            AddRolePermissions(sources, role.Permissions, new PermissionSource(
                "team_role", role.Slug, role.Name, role.Id, team.Id, team.Name, null, null,
                assignment.AssignedByUserId, assignment.AssignedAt, assignment.ExpiresAt),
                ScopeType.Team);
        }

        foreach (var grant in store.ListPermissionGrantsForUser(userId)
                     .Where(g => !g.IsExpired(now) && g.ScopeType == ScopeType.Team && g.ScopeId == teamId))
        {
            AddPermission(sources, grant.PermissionId, new PermissionSource(
                "direct_grant", null, null, null, team.Id, team.Name, null, null,
                grant.GrantedByUserId, grant.GrantedAt, grant.ExpiresAt));
        }
    }

    private void AddProjectScoped(
        Dictionary<string, List<PermissionSource>> sources,
        Guid userId,
        OrganisationRole orgRole,
        Guid projectId,
        DateTimeOffset now)
    {
        var project = store.FindProject(projectId);
        if (project is null)
        {
            return;
        }

        if (access is not null && !access.CanAccess(project, userId, orgRole))
        {
            return;
        }

        // Team memberships contribute team-scoped permissions that may include team.projects.*.
        foreach (var teamMembership in ActiveTeamMemberships(userId, now))
        {
            AddTeamScoped(sources, userId, teamMembership.TeamId, now);
        }

        foreach (var grant in store.ListProjectUserAccess(projectId)
                     .Where(g => g.UserId == userId && g.IsEffectivelyActive(now)))
        {
            if (RolePermissions(grant.RoleId) is { } role)
            {
                AddRolePermissions(sources, role.Permissions, new PermissionSource(
                    "project_role", role.Slug, role.Name, role.Id, null, null, project.Id, project.Name,
                    grant.GrantedByUserId, grant.GrantedAt, grant.ExpiresAt),
                    ScopeType.Project);
            }
        }

        var teamGrants = store.ListProjectTeamAccess(projectId).Where(g => g.IsEffectivelyActive(now)).ToArray();
        foreach (var teamMembership in ActiveTeamMemberships(userId, now))
        {
            var grant = teamGrants.FirstOrDefault(g => g.TeamId == teamMembership.TeamId);
            if (grant is null)
            {
                continue;
            }

            var team = store.FindTeam(teamMembership.TeamId);
            var roleId = grant.RoleId
                         ?? team?.RoleId
                         ?? ResolveDefaultProjectRoleFromTeamAssignments(userId, teamMembership.TeamId, now);
            if (RolePermissions(roleId) is not { } role)
            {
                continue;
            }

            AddRolePermissions(sources, role.Permissions, new PermissionSource(
                "team_project_access", role.Slug, role.Name, role.Id,
                teamMembership.TeamId, team?.Name, project.Id, project.Name,
                grant.GrantedByUserId, grant.GrantedAt, grant.ExpiresAt),
                ScopeType.Project);
        }

        foreach (var grant in store.ListPermissionGrantsForUser(userId)
                     .Where(g => !g.IsExpired(now) && g.ScopeType == ScopeType.Project && g.ScopeId == projectId))
        {
            AddPermission(sources, grant.PermissionId, new PermissionSource(
                "direct_grant", null, null, null, null, null, project.Id, project.Name,
                grant.GrantedByUserId, grant.GrantedAt, grant.ExpiresAt));
        }
    }

    private Guid? ResolveDefaultProjectRoleFromTeamAssignments(Guid userId, Guid teamId, DateTimeOffset now)
    {
        foreach (var assignment in store.ListRoleAssignments(userId, ScopeType.Team, teamId).Where(a => !a.IsExpired(now)))
        {
            var teamRole = store.FindAccessRole(assignment.RoleId);
            if (teamRole?.DefaultProjectRoleId is Guid defaultId)
            {
                return defaultId;
            }
        }

        return null;
    }

    private IEnumerable<TeamMembership> ActiveTeamMemberships(Guid userId, DateTimeOffset now) =>
        store.ListUserTeamMemberships(userId).Where(m => m.IsEffectivelyActive(now));

    private (string Slug, string Name, Guid Id, IReadOnlySet<string> Permissions)? RolePermissions(Guid? roleId)
    {
        if (roleId is not Guid id)
        {
            return null;
        }

        var role = store.FindAccessRole(id);
        return role is null ? null : (role.Slug, role.Name, role.Id, role.Permissions);
    }

    private static void AddRolePermissions(
        Dictionary<string, List<PermissionSource>> sources,
        IEnumerable<string> permissions,
        PermissionSource source,
        ScopeType assignmentScope)
    {
        foreach (var permission in permissions)
        {
            // Organisation role bundles may include project module permissions. Team/project roles must not
            // smuggle organisation-only permissions into a narrower scope.
            if (assignmentScope != ScopeType.Organisation
                && PermissionCatalogue.Find(permission) is { } descriptor
                && !descriptor.AllowedScopes.Contains(assignmentScope))
            {
                continue;
            }

            AddPermission(sources, permission, source);
        }
    }

    private static void AddPermission(
        Dictionary<string, List<PermissionSource>> sources,
        string permission,
        PermissionSource source)
    {
        if (!sources.TryGetValue(permission, out var list))
        {
            list = [];
            sources[permission] = list;
        }

        list.Add(source);
    }
}
