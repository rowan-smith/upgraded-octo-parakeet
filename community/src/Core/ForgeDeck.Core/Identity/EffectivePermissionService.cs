using ForgeDeck.Core.Application;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Identity;

/// <summary>
/// Compatibility façade over <see cref="PermissionService"/>. Effective permissions are the additive union of
/// applicable organisation, team, and project grants — not an organisation ceiling intersected with grant roles.
/// </summary>
public sealed class EffectivePermissionService
{
    private readonly PermissionService _permissions;

    public EffectivePermissionService(ITenancyStore store, RoleService roles, ProjectAccessService? access = null)
        : this(store, roles, new PermissionDefinitionRegistry(), access)
    {
    }

    public EffectivePermissionService(
        ITenancyStore store,
        RoleService roles,
        IPermissionDefinitionRegistry registry,
        ProjectAccessService? access = null)
    {
        _permissions = new PermissionService(store, roles, registry, access);
    }

    public EffectivePermissionService(PermissionService permissions) => _permissions = permissions;

    public IReadOnlySet<string> ForUser(Guid userId, Guid? projectId = null) =>
        _permissions.ForUser(userId, projectId);

    public bool Has(Guid userId, string permission, Guid? projectId = null) =>
        _permissions.Has(userId, permission, projectId);

    public PermissionService Service => _permissions;
}
