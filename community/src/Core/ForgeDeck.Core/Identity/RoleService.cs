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

    public AccessRole Create(string name, string? slug, IEnumerable<string>? permissions, string? description = null)
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

        var role = new AccessRole
        {
            Name = name.Trim(),
            Slug = normalized,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Permissions = PermissionCatalogue.Normalise(permissions)
        };
        store.SaveAccessRole(role);
        return role;
    }

    public AccessRole Update(Guid id, string? name, IEnumerable<string>? permissions, string? description = null, string? slug = null)
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
                    Permissions = new HashSet<string>(definition.Permissions, StringComparer.OrdinalIgnoreCase)
                });
            }

            _seeded = true;
        }
    }
}
