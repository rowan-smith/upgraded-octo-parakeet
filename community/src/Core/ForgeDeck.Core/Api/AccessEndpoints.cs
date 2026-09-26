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

        group.MapGet("/permissions", () => Results.Ok(PermissionCatalogue.All.Select(p => new
        {
            key = p.Key,
            category = p.Category,
            title = p.Title,
            description = p.Description
        })));

        group.MapGet("/roles", (RoleService roles, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.UsersRead) &&
                !authorizer.Has(http, OrganisationPermissions.TeamsManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(roles.List().Select(RoleDto));
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
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                var role = roles.Create(request.Name, request.Slug, request.Permissions ?? [], request.Description);
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
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                return Results.Ok(RoleDto(roles.Update(id, request.Name, request.Permissions, request.Description)));
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
            if (!authorizer.Has(http, OrganisationPermissions.UsersManage))
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

        group.MapGet("/effective", (EffectivePermissionService effective, PlatformContextStore context, ITenancyStore store) =>
        {
            var projectId = context.Project?.Id;
            var permissions = effective.ForUser(context.User.Id, projectId);
            return Results.Ok(new
            {
                userId = context.User.Id,
                projectId,
                permissions = permissions.OrderBy(p => p).ToArray()
            });
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

    private static object RoleDto(AccessRole role) => new
    {
        id = role.Id,
        name = role.Name,
        slug = role.Slug,
        description = role.Description,
        isSystem = role.IsSystem,
        permissions = role.Permissions.OrderBy(p => p).ToArray()
    };
}

public sealed record CreateAccessRoleRequest(string Name, string? Slug, string? Description, IReadOnlyList<string>? Permissions);
public sealed record UpdateAccessRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);
public sealed record AssignRoleRequest(Guid? RoleId);
