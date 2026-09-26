using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Identity;

/// <summary>
/// Resolves the permissions a user actually holds. The organisation membership role is always the ceiling; when a
/// project context exists, the union of the roles attached to that user's applicable project and team grants narrows
/// the ceiling further. Grants without a role do not narrow anything.
/// </summary>
public sealed class EffectivePermissionService(ITenancyStore store, RoleService roles, ProjectAccessService? access = null)
{
    private static readonly HashSet<string> None = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ForUser(Guid userId, Guid? projectId = null)
    {
        var membership = store.GetMembership(userId);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            return None;
        }

        var ceiling = OrganisationPermissions.ForRole(membership.Role);
        if (projectId is not Guid id)
        {
            return ceiling;
        }

        var project = store.FindProject(id);
        if (project is null)
        {
            return ceiling;
        }

        // A project the user cannot reach contributes nothing; project access itself is enforced by ProjectAccessService.
        if (access is not null && !access.CanAccess(project, userId, membership.Role))
        {
            return ceiling;
        }

        roles.EnsureSystemRoles();
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var narrowed = false;

        foreach (var grant in store.ListProjectUserAccess(id).Where(row => row.UserId == userId))
        {
            if (Permissions(grant.RoleId) is { } permissions)
            {
                granted.UnionWith(permissions);
                narrowed = true;
            }
        }

        var teamGrants = store.ListProjectTeamAccess(id);
        foreach (var teamMembership in store.ListUserTeamMemberships(userId))
        {
            var grant = teamGrants.FirstOrDefault(row => row.TeamId == teamMembership.TeamId);
            if (grant is null)
            {
                continue;
            }

            // The grant's own role wins; otherwise the role attached to the group applies.
            if (Permissions(grant.RoleId ?? store.FindTeam(teamMembership.TeamId)?.RoleId) is { } permissions)
            {
                granted.UnionWith(permissions);
                narrowed = true;
            }
        }

        // No role anywhere on the applicable grants means nothing narrows the organisation ceiling.
        return narrowed ? Intersect(ceiling, granted) : ceiling;
    }

    public bool Has(Guid userId, string permission, Guid? projectId = null) =>
        ForUser(userId, projectId).Contains(permission);

    public static IReadOnlySet<string> Intersect(IReadOnlySet<string> ceiling, IEnumerable<string> granted)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in granted)
        {
            if (ceiling.Contains(permission))
            {
                result.Add(permission);
            }
        }

        return result;
    }

    private IReadOnlySet<string>? Permissions(Guid? roleId) =>
        roleId is Guid id ? store.FindAccessRole(id)?.Permissions : null;
}
