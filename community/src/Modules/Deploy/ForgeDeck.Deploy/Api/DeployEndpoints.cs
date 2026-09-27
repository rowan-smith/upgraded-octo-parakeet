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

        group.MapGet("/agents", ListAgents);
        group.MapPost("/agents/register", RegisterAgent);
        group.MapPost("/agents/{id:guid}/heartbeat", AgentHeartbeat);
        group.MapPost("/agents/{id:guid}/work", AgentClaimWork);
        group.MapPost("/agents/{id:guid}/complete", AgentComplete);
        group.MapPost("/agents/{agentId:guid}/deployments/{deploymentId:guid}/complete", AgentCompleteDeployment);
        group.MapDelete("/agents/{id:guid}", RevokeAgent);
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

    private static IResult ListAgents(HttpContext context, DeployAgentService agents, PermissionAuthorizer authorizer)
    {
        if (!authorizer.Has(context, "deploy.read") && !authorizer.Has(context, PlatformPermissions.DeployRead)
            && !authorizer.Has(context, "deploy.manage") && !authorizer.Has(context, PlatformPermissions.DeployExecute))
        {
            return PermissionAuthorizer.Forbidden();
        }

        return Results.Ok(agents.ListAgents());
    }

    private static IResult RegisterAgent(RegisterAgentRequest request, DeployAgentService agents, IAuditWriter audit)
    {
        try
        {
            var (agentId, agentToken) = agents.Register(request.Name, request.Labels, request.RegistrationToken);
            audit.Write("deploy", "agent.registered", agentId.ToString());
            return Results.Ok(new
            {
                agentId,
                agentToken,
                warning = "Store the deploy agent token securely; it cannot be retrieved again."
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult AgentHeartbeat(Guid id, AgentHeartbeatRequest? request, HttpRequest http, DeployAgentService agents)
    {
        try
        {
            var token = RequireAgentToken(http);
            var agent = agents.Heartbeat(id, token, request?.Labels);
            return Results.Ok(new
            {
                agent.Id,
                agent.Name,
                status = agent.Status.ToString(),
                health = agent.IsHealthy() ? "Healthy" : "Offline",
                agent.Labels,
                agent.LastHeartbeatAt
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static IResult AgentClaimWork(Guid id, AgentWorkRequest? request, HttpRequest http, DeployAgentService agents)
    {
        try
        {
            var token = RequireAgentToken(http);
            return Results.Ok(agents.ClaimWork(id, token, request?.MaxJobs ?? 1));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static IResult AgentComplete(Guid id, AgentCompleteRequest request, HttpRequest http, DeployAgentService agents)
    {
        try
        {
            var token = RequireAgentToken(http);
            var deployment = agents.Complete(id, token, request.DeploymentId, request.Success, request.Log);
            return Results.Ok(deployment);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
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

    private static IResult AgentCompleteDeployment(
        Guid agentId,
        Guid deploymentId,
        AgentCompleteBody? request,
        HttpRequest http,
        DeployAgentService agents)
    {
        try
        {
            var token = RequireAgentToken(http);
            var deployment = agents.Complete(agentId, token, deploymentId, request?.Success ?? true, request?.Log);
            return Results.Ok(deployment);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
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

    private static IResult RevokeAgent(
        Guid id,
        HttpContext context,
        DeployAgentService agents,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "deploy.manage") && !authorizer.Has(context, PlatformPermissions.DeployExecute))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            agents.Revoke(id);
            audit.Write("deploy", "agent.revoked", id.ToString());
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static string RequireAgentToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Deploy-Agent-Token", out var values) || string.IsNullOrWhiteSpace(values))
        {
            throw new UnauthorizedAccessException("Missing deploy agent token.");
        }

        return values.ToString();
    }

    private sealed record CreateEnvironmentRequest(string Name, string? Description);
    private sealed record CreateDeploymentRequest(Guid EnvironmentId, string Version, string? Notes);
    private sealed record RegisterAgentRequest(string Name, IReadOnlyList<string>? Labels, string? RegistrationToken);
    private sealed record AgentHeartbeatRequest(IReadOnlyList<string>? Labels);
    private sealed record AgentWorkRequest(int MaxJobs = 1);
    private sealed record AgentCompleteRequest(Guid DeploymentId, bool Success, string? Log);
    private sealed record AgentCompleteBody(bool Success = true, string? Log = null);
}
