using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Identity;

/// <summary>Custom and built-in access roles. The built-in set is seeded lazily on first read or write.</summary>
public sealed class RoleService(ITenancyStore store)
{
    private readonly object _seedGate = new();
    private bool _seeded;

    public IReadOnlyList<AccessRole> List()
    {
        EnsureSystemRoles();
        return store.ListAccessRoles();
    }

    public IReadOnlyList<AccessRole> List(ScopeType scopeType, Guid? ownerId = null)
    {
        EnsureSystemRoles();
        return store.ListAccessRoles(scopeType, ownerId);
    }

    public AccessRole? Find(Guid id)
    {
        EnsureSystemRoles();
        return store.FindAccessRole(id);
    }

    public AccessRole? FindBySlug(string slug)
    {
        EnsureSystemRoles();
        return store.FindAccessRoleBySlug(slug);
    }

    public AccessRole Create(
        string name,
        string? slug,
        IEnumerable<string>? permissions,
        string? description = null,
        ScopeType scopeType = ScopeType.Organisation,
        RoleOwnerType? ownerType = null,
        Guid? ownerId = null,
        Guid? defaultProjectRoleId = null)
    {
        EnsureSystemRoles();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Role name is required.");
        }

        var normalized = string.IsNullOrWhiteSpace(slug) ? SlugRules.Normalize(name) : SlugRules.Normalize(slug);
        if (!SlugRules.IsValid(normalized))
        {
            throw new ArgumentException("Role slug is invalid.");
        }

        if (store.FindAccessRoleBySlug(normalized) is not null)
        {
            throw new ArgumentException("Role slug is already in use.");
        }

        if (defaultProjectRoleId is Guid defaultId && store.FindAccessRole(defaultId) is null)
        {
            throw new KeyNotFoundException("Default project role not found.");
        }

        var role = new AccessRole
        {
            Name = name.Trim(),
            Slug = normalized,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Permissions = PermissionCatalogue.Normalise(permissions),
            ScopeType = scopeType,
            OwnerType = ownerType,
            OwnerId = ownerId,
            DefaultProjectRoleId = defaultProjectRoleId
        };
        store.SaveAccessRole(role);
        return role;
    }

    public AccessRole Update(
        Guid id,
        string? name,
        IEnumerable<string>? permissions,
        string? description = null,
        string? slug = null,
        Guid? defaultProjectRoleId = null,
        bool clearDefaultProjectRole = false)
    {
        EnsureSystemRoles();
        var role = store.FindAccessRole(id) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystem)
        {
            throw new InvalidOperationException("Built-in roles cannot be edited.");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            role.Name = name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = SlugRules.Normalize(slug);
            if (!SlugRules.IsValid(normalized))
            {
                throw new ArgumentException("Role slug is invalid.");
            }

            var existing = store.FindAccessRoleBySlug(normalized);
            if (existing is not null && existing.Id != id)
            {
                throw new ArgumentException("Role slug is already in use.");
            }

            role.Slug = normalized;
        }

        if (description is not null)
        {
            role.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        if (permissions is not null)
        {
            role.Permissions = PermissionCatalogue.Normalise(permissions);
        }

        if (clearDefaultProjectRole)
        {
            role.DefaultProjectRoleId = null;
        }
        else if (defaultProjectRoleId is Guid defaultId)
        {
            if (store.FindAccessRole(defaultId) is null)
            {
                throw new KeyNotFoundException("Default project role not found.");
            }

            role.DefaultProjectRoleId = defaultId;
        }

        role.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveAccessRole(role);
        return role;
    }

    public void Delete(Guid id)
    {
        EnsureSystemRoles();
        var role = store.FindAccessRole(id) ?? throw new KeyNotFoundException("Role not found.");
        if (role.IsSystem)
        {
            throw new InvalidOperationException("Built-in roles cannot be deleted.");
        }

        store.DeleteAccessRole(id);
    }

    public void AssignTeamRole(Guid teamId, Guid? roleId)
    {
        EnsureSystemRoles();
        var team = store.FindTeam(teamId) ?? throw new KeyNotFoundException("Team not found.");
        team.RoleId = Require(roleId);
        team.UpdatedAt = DateTimeOffset.UtcNow;
        store.SaveTeam(team);
    }

    public RoleAssignment AssignRole(
        Guid userId,
        Guid roleId,
        ScopeType scopeType,
        Guid? scopeId,
        Guid? assignedByUserId = null,
        DateTimeOffset? expiresAt = null)
    {
        EnsureSystemRoles();
        var role = store.FindAccessRole(roleId) ?? throw new KeyNotFoundException("Role not found.");
        if (role.ScopeType != scopeType)
        {
            throw new ArgumentException($"Role '{role.Slug}' cannot be assigned in {scopeType} scope.");
        }

        if (scopeType != ScopeType.Organisation && scopeId is null)
        {
            throw new ArgumentException("Scope id is required for team and project role assignments.");
        }

        if (scopeType == ScopeType.Team && scopeId is Guid teamId)
        {
            _ = store.FindTeam(teamId) ?? throw new KeyNotFoundException("Team not found.");
            if (store.ListUserTeamMemberships(userId).All(m => m.TeamId != teamId))
            {
                throw new InvalidOperationException("User must be a team member before receiving a team role.");
            }
        }

        if (scopeType == ScopeType.Project && scopeId is Guid projectId)
        {
            _ = store.FindProject(projectId) ?? throw new KeyNotFoundException("Project not found.");
        }

        // Replace existing assignment of the same role in the same scope.
        foreach (var existing in store.ListRoleAssignments(userId, scopeType, scopeId)
                     .Where(a => a.RoleId == roleId))
        {
            store.DeleteRoleAssignment(existing.Id);
        }

        var assignment = new RoleAssignment
        {
            UserId = userId,
            RoleId = roleId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            AssignedByUserId = assignedByUserId,
            ExpiresAt = expiresAt
        };
        store.SaveRoleAssignment(assignment);
        return assignment;
    }

    public PermissionGrant GrantPermission(
        Guid userId,
        string permissionId,
        ScopeType scopeType,
        Guid? scopeId,
        Guid? grantedByUserId = null,
        DateTimeOffset? expiresAt = null)
    {
        EnsureSystemRoles();
        var normalised = PermissionCatalogue.Normalise([permissionId]).First();
        if (!PermissionCatalogue.AllowsScope(normalised, scopeType))
        {
            throw new ArgumentException($"Permission '{normalised}' cannot be granted at {scopeType} scope.");
        }

        if (store.GetMembership(userId) is null)
        {
            throw new KeyNotFoundException("User is not an organisation member.");
        }

        var grant = new PermissionGrant
        {
            UserId = userId,
            PermissionId = normalised,
            ScopeType = scopeType,
            ScopeId = scopeId,
            GrantedByUserId = grantedByUserId,
            ExpiresAt = expiresAt
        };
        store.SavePermissionGrant(grant);
        return grant;
    }

    public void RevokePermissionGrant(Guid grantId) => store.DeletePermissionGrant(grantId);

    /// <summary>Validates an optional role reference before it is attached to a team or grant.</summary>
    public Guid? Require(Guid? roleId)
    {
        if (roleId is not Guid id)
        {
            return null;
        }

        EnsureSystemRoles();
        if (store.FindAccessRole(id) is null)
        {
            throw new KeyNotFoundException("Role not found.");
        }

        return id;
    }

    public void EnsureSystemRoles()
    {
        if (_seeded)
        {
            return;
        }

        lock (_seedGate)
        {
            if (_seeded)
            {
                return;
            }

            // First pass: create roles without default project role links.
            foreach (var definition in SystemAccessRoles.Definitions)
            {
                if (store.FindAccessRoleBySlug(definition.Slug) is not null)
                {
                    continue;
                }

                store.SaveAccessRole(new AccessRole
                {
                    Name = definition.Name,
                    Slug = definition.Slug,
                    Description = definition.Description,
                    IsSystem = true,
                    ScopeType = definition.ScopeType,
                    Permissions = new HashSet<string>(definition.Permissions, StringComparer.OrdinalIgnoreCase)
                });
            }

            // Second pass: wire default project role links and refresh permissions for existing system roles.
            foreach (var definition in SystemAccessRoles.Definitions)
            {
                var role = store.FindAccessRoleBySlug(definition.Slug);
                if (role is null || !role.IsSystem)
                {
                    continue;
                }

                role.Permissions = new HashSet<string>(definition.Permissions, StringComparer.OrdinalIgnoreCase);
                role.ScopeType = definition.ScopeType;
                if (definition.DefaultProjectRoleSlug is string defaultSlug)
                {
                    role.DefaultProjectRoleId = store.FindAccessRoleBySlug(defaultSlug)?.Id;
                }

                role.UpdatedAt = DateTimeOffset.UtcNow;
                store.SaveAccessRole(role);
            }

            _seeded = true;
        }
    }
}
