using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;
using ForgeDeck.Core.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Core.Api;

public static class AccessEndpoints
{
    public static void MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/access");

        group.MapGet("/permissions", (IPermissionDefinitionRegistry registry) => Results.Ok(registry.ListActive().Select(p => new
        {
            key = p.Key,
            category = p.Category,
            title = p.Title,
            description = p.Description,
            allowedScopes = p.AllowedScopes.Select(s => s.ToString()).ToArray(),
            sourceExtensionId = p.SourceExtensionId,
            riskLevel = p.RiskLevel
        })));

        group.MapGet("/permissions/catalogue", (IPermissionDefinitionRegistry registry, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead) &&
                !authorizer.Has(http, OrganisationPermissions.PermissionsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(registry.ListAll().Select(p => new
            {
                key = p.Key,
                category = p.Category,
                title = p.Title,
                description = p.Description,
                allowedScopes = p.AllowedScopes.Select(s => s.ToString()).ToArray(),
                sourceExtensionId = p.SourceExtensionId,
                active = registry.IsActive(p.Key)
            }));
        });

        group.MapGet("/roles", (RoleService roles, PermissionAuthorizer authorizer, HttpContext http, ScopeType? scopeType, Guid? ownerId) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead) &&
                !authorizer.Has(http, OrganisationPermissions.TeamsManage) &&
                !authorizer.Has(http, OrganisationPermissions.TeamRolesRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var list = scopeType is ScopeType scope ? roles.List(scope, ownerId) : roles.List();
            return Results.Ok(list.Select(RoleDto));
        });

        group.MapGet("/roles/{id:guid}", (Guid id, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return roles.Find(id) is { } role ? Results.Ok(RoleDto(role)) : Results.NotFound();
        });

        group.MapPost("/roles", (CreateAccessRoleRequest request, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage) &&
                !authorizer.Has(http, OrganisationPermissions.RolesManage) &&
                !authorizer.Has(http, OrganisationPermissions.TeamRolesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                var role = roles.Create(
                    request.Name,
                    request.Slug,
                    request.Permissions ?? [],
                    request.Description,
                    request.ScopeType ?? ScopeType.Organisation,
                    request.OwnerType,
                    request.OwnerId,
                    request.DefaultProjectRoleId);
                return Results.Created($"/api/access/roles/{role.Id}", RoleDto(role));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        group.MapPatch("/roles/{id:guid}", (Guid id, UpdateAccessRoleRequest request, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage) &&
                !authorizer.Has(http, OrganisationPermissions.RolesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                return Results.Ok(RoleDto(roles.Update(
                    id,
                    request.Name,
                    request.Permissions,
                    request.Description,
                    request.Slug,
                    request.DefaultProjectRoleId,
                    request.ClearDefaultProjectRole == true)));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/roles/{id:guid}", (Guid id, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage) &&
                !authorizer.Has(http, OrganisationPermissions.RolesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                roles.Delete(id);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/effective", async (
            IPermissionService permissions,
            PlatformContextStore context,
            PermissionAuthorizer authorizer,
            HttpContext http,
            string? scopeType,
            Guid? scopeId,
            Guid? userId) =>
        {
            var targetUserId = userId ?? context.User.Id;
            if (targetUserId != context.User.Id
                && !authorizer.Has(http, OrganisationPermissions.UsersManage)
                && !authorizer.Has(http, OrganisationPermissions.PermissionsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var scope = ParseScope(scopeType, scopeId) ??
                        (context.Project?.Id is Guid projectId
                            ? ResourceScope.ForProject(projectId)
                            : ResourceScope.Organisation);
            var set = await permissions.GetEffectivePermissionsAsync(targetUserId, scope);
            return Results.Ok(new
            {
                userId = targetUserId,
                projectId = scope.Type == ScopeType.Project ? scope.Id : null,
                scopeType = scope.Type.ToString(),
                scopeId = scope.Id,
                permissions = set.Permissions.OrderBy(p => p).ToArray(),
                sources = set.Sources.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Select(SourceDto).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            });
        });

        group.MapGet("/explain", async (
            IPermissionService permissions,
            PlatformContextStore context,
            PermissionAuthorizer authorizer,
            HttpContext http,
            string permission,
            string? scopeType,
            Guid? scopeId,
            Guid? userId) =>
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                return Results.BadRequest(new { error = "permission is required." });
            }

            var targetUserId = userId ?? context.User.Id;
            if (targetUserId != context.User.Id
                && !authorizer.Has(http, OrganisationPermissions.UsersManage)
                && !authorizer.Has(http, OrganisationPermissions.PermissionsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var scope = ParseScope(scopeType, scopeId) ??
                        (context.Project?.Id is Guid projectId
                            ? ResourceScope.ForProject(projectId)
                            : ResourceScope.Organisation);
            var explanation = await permissions.ExplainAsync(targetUserId, permission.Trim(), scope);
            return Results.Ok(new
            {
                userId = targetUserId,
                permission = explanation.PermissionId,
                granted = explanation.Granted,
                scopeType = explanation.Scope.Type.ToString(),
                scopeId = explanation.Scope.Id,
                sources = explanation.Sources.Select(SourceDto).ToArray()
            });
        });

        group.MapGet("/grants", (ITenancyStore store, PermissionAuthorizer authorizer, HttpContext http, ScopeType scopeType, Guid? scopeId) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.PermissionsManage) &&
                !authorizer.Has(http, OrganisationPermissions.UsersManage) &&
                !authorizer.Has(http, OrganisationPermissions.ProjectPermissionsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(store.ListPermissionGrants(scopeType, scopeId).Select(g => new
            {
                id = g.Id,
                userId = g.UserId,
                permissionId = g.PermissionId,
                scopeType = g.ScopeType.ToString(),
                scopeId = g.ScopeId,
                expiresAt = g.ExpiresAt,
                grantedAt = g.GrantedAt,
                grantedByUserId = g.GrantedByUserId
            }));
        });

        group.MapPost("/grants", (CreatePermissionGrantRequest request, RoleService roles, PermissionAuthorizer authorizer, HttpContext http, PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.PermissionsManage) &&
                !authorizer.Has(http, OrganisationPermissions.UsersManage) &&
                !authorizer.Has(http, OrganisationPermissions.ProjectPermissionsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                var grant = roles.GrantPermission(
                    request.UserId,
                    request.PermissionId,
                    request.ScopeType,
                    request.ScopeId,
                    context.User.Id,
                    request.ExpiresAt);
                return Results.Created($"/api/access/grants/{grant.Id}", new
                {
                    id = grant.Id,
                    userId = grant.UserId,
                    permissionId = grant.PermissionId,
                    scopeType = grant.ScopeType.ToString(),
                    scopeId = grant.ScopeId,
                    expiresAt = grant.ExpiresAt
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete("/grants/{id:guid}", (Guid id, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.PermissionsManage) &&
                !authorizer.Has(http, OrganisationPermissions.UsersManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            roles.RevokePermissionGrant(id);
            return Results.NoContent();
        });

        app.MapPatch("/api/teams/{id:guid}/role", (Guid id, AssignRoleRequest request, RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.TeamsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                roles.AssignTeamRole(id, request.RoleId);
                return Results.Ok(new { teamId = id, roleId = request.RoleId });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }

    private static ResourceScope? ParseScope(string? scopeType, Guid? scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
        {
            return null;
        }

        if (!Enum.TryParse<ScopeType>(scopeType, true, out var type))
        {
            return null;
        }

        return type switch
        {
            ScopeType.Organisation => ResourceScope.Organisation,
            ScopeType.Team when scopeId is Guid teamId => ResourceScope.ForTeam(teamId),
            ScopeType.Project when scopeId is Guid projectId => ResourceScope.ForProject(projectId),
            _ => null
        };
    }

    private static object RoleDto(AccessRole role) => new
    {
        id = role.Id,
        name = role.Name,
        slug = role.Slug,
        description = role.Description,
        isSystem = role.IsSystem,
        scopeType = role.ScopeType.ToString(),
        ownerType = role.OwnerType?.ToString(),
        ownerId = role.OwnerId,
        defaultProjectRoleId = role.DefaultProjectRoleId,
        permissions = role.Permissions.OrderBy(p => p).ToArray()
    };

    private static object SourceDto(PermissionSource source) => new
    {
        kind = source.Kind,
        roleSlug = source.RoleSlug,
        roleName = source.RoleName,
        roleId = source.RoleId,
        teamId = source.TeamId,
        teamName = source.TeamName,
        projectId = source.ProjectId,
        projectName = source.ProjectName,
        grantedByUserId = source.GrantedByUserId,
        grantedAt = source.GrantedAt,
        expiresAt = source.ExpiresAt
    };
}

public sealed record CreateAccessRoleRequest(
    string Name,
    string? Slug,
    string? Description,
    IReadOnlyList<string>? Permissions,
    ScopeType? ScopeType = null,
    RoleOwnerType? OwnerType = null,
    Guid? OwnerId = null,
    Guid? DefaultProjectRoleId = null);

public sealed record UpdateAccessRoleRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    string? Slug = null,
    Guid? DefaultProjectRoleId = null,
    bool? ClearDefaultProjectRole = null);

public sealed record AssignRoleRequest(Guid? RoleId);

public sealed record CreatePermissionGrantRequest(
    Guid UserId,
    string PermissionId,
    ScopeType ScopeType,
    Guid? ScopeId = null,
    DateTimeOffset? ExpiresAt = null);
