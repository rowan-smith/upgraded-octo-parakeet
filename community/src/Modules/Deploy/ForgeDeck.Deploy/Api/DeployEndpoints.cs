using ForgeDeck.Contracts.Audit;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Identity;
using ForgeDeck.Deploy.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Deploy.Api;

public static class DeployEndpoints
{
    public static void MapDeployEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/deploy");

        group.MapGet("/environments", (DeployService service, PlatformContextStore platform) =>
            Results.Ok(service.ListEnvironments(platform.Project.Id)));
        group.MapGet("/environments/{id:guid}", (Guid id, DeployService service) =>
            service.FindEnvironment(id) is { } env ? Results.Ok(env) : Results.NotFound());
        group.MapPost("/environments", CreateEnvironment);
        group.MapDelete("/environments/{id:guid}", DeleteEnvironment);

        group.MapGet("/deployments", ListDeployments);
        group.MapGet("/deployments/{id:guid}", (Guid id, DeployService service) =>
            service.FindDeployment(id) is { } deployment ? Results.Ok(deployment) : Results.NotFound());
        group.MapPost("/deployments", CreateDeployment);
        group.MapPost("/deployments/{id:guid}/rollback", Rollback);
    }

    private static IResult CreateEnvironment(
        CreateEnvironmentRequest request,
        HttpContext context,
        DeployService service,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "deploy.manage") && !authorizer.Has(context, PlatformPermissions.DeployExecute))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var environment = service.CreateEnvironment(platform.Project.Id, request.Name, request.Description);
            audit.Write("deploy", "environment.created", environment.Id.ToString());
            return Results.Created($"/api/deploy/environments/{environment.Id}", environment);
        }
        catch (LicenceRequiredException exception)
        {
            return CapabilityAuthorizer.Forbidden(exception.Capability);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult DeleteEnvironment(
        Guid id,
        HttpContext context,
        DeployService service,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "deploy.manage"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            service.DeleteEnvironment(id);
            audit.Write("deploy", "environment.deleted", id.ToString());
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static IResult ListDeployments(
        DeployService service,
        PlatformContextStore platform,
        Guid? environmentId)
    {
        return Results.Ok(service.ListDeployments(platform.Project.Id, environmentId));
    }

    private static IResult CreateDeployment(
        CreateDeploymentRequest request,
        HttpContext context,
        DeployService service,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "deploy.execute") && !authorizer.Has(context, PlatformPermissions.DeployExecute))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var user = context.User.Identity?.Name ?? "system";
            var deployment = service.CreateDeployment(
                platform.Project.Id,
                request.EnvironmentId,
                request.Version,
                user,
                request.Notes);
            audit.Write("deploy", "deployment.created", deployment.Id.ToString());
            return Results.Created($"/api/deploy/deployments/{deployment.Id}", deployment);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult Rollback(
        Guid id,
        HttpContext context,
        DeployService service,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "deploy.execute") && !authorizer.Has(context, PlatformPermissions.DeployExecute))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var user = context.User.Identity?.Name ?? "system";
            var deployment = service.Rollback(id, user);
            audit.Write("deploy", "deployment.rollback", deployment.Id.ToString());
            return Results.Ok(deployment);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private sealed record CreateEnvironmentRequest(string Name, string? Description);
    private sealed record CreateDeploymentRequest(Guid EnvironmentId, string Version, string? Notes);
}
