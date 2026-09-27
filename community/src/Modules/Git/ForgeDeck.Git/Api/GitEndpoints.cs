using ForgeDeck.Contracts.Audit;
using ForgeDeck.Core.Identity;
using ForgeDeck.Git.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Git.Api;

public static class GitEndpoints
{
    public static void MapGitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/git");
        group.MapGet("/repositories", (GitRepositoryService service) => Results.Ok(service.List()));
        group.MapGet("/repositories/{id:guid}", (Guid id, GitRepositoryService service) => service.Find(id) is { } repository ? Results.Ok(repository) : Results.NotFound());
        group.MapGet("/repositories/{id:guid}/branches", (Guid id, GitRepositoryService service) => service.Find(id) is { } repository ? Results.Ok(repository.Branches) : Results.NotFound());
        group.MapGet("/repositories/{id:guid}/commits", (Guid id, GitRepositoryService service) => service.Find(id) is { } repository ? Results.Ok(repository.Commits) : Results.NotFound());
        group.MapGet("/repositories/{id:guid}/tags", (Guid id, GitRepositoryService service) => service.Find(id) is { } repository ? Results.Ok(repository.Tags) : Results.NotFound());
        group.MapGet("/repositories/{id:guid}/clone-url", GetCloneUrl);
        group.MapPost("/repositories/{id:guid}/fetch", FetchRefs);
        group.MapGet("/repositories/{id:guid}/info/refs", AdvertiseRefs);
        group.MapPost("/repositories/{id:guid}/git-upload-pack", UploadPack);
        group.MapPost("/repositories/{id:guid}/git-receive-pack", ReceivePack);
        group.MapPost("/repositories", CreateRepository);
        group.MapPost("/repositories/{id:guid}/push", Push);
    }

    private static IResult GetCloneUrl(Guid id, HttpRequest request, GitRepositoryService service)
    {
        var baseUrl = $"{request.Scheme}://{request.Host.Value}";
        var info = service.GetCloneUrl(id, baseUrl);
        return info is null ? Results.NotFound() : Results.Ok(info);
    }

    private static IResult FetchRefs(Guid id, GitRepositoryService service)
    {
        var info = service.GetFetchInfo(id);
        return info is null ? Results.NotFound() : Results.Ok(info);
    }

    private static async Task<IResult> AdvertiseRefs(
        Guid id,
        HttpRequest request,
        GitSmartHttpTransport transport,
        CancellationToken cancellationToken)
    {
        if (!request.Query.TryGetValue("service", out var serviceValues)
            || string.IsNullOrWhiteSpace(serviceValues))
        {
            return Results.BadRequest(new { error = "Missing service query parameter." });
        }

        try
        {
            var (contentType, body) = await transport.AdvertiseAsync(id, serviceValues.ToString(), cancellationToken);
            return Results.Bytes(body, contentType);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UploadPack(
        Guid id,
        HttpRequest request,
        GitSmartHttpTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            var (contentType, body) = await transport.HandleRpcAsync(id, "git-upload-pack", request.Body, cancellationToken);
            return Results.Bytes(body, contentType);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
        }
    }

    private static async Task<IResult> ReceivePack(
        Guid id,
        HttpRequest request,
        GitSmartHttpTransport transport,
        PermissionAuthorizer authorizer,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!authorizer.Has(context, "git.repository.push"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var (contentType, body) = await transport.HandleRpcAsync(id, "git-receive-pack", request.Body, cancellationToken);
            return Results.Bytes(body, contentType);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status501NotImplemented);
        }
    }

    private static IResult CreateRepository(CreateRepositoryRequest request, HttpContext context, GitRepositoryService service, PermissionAuthorizer authorizer, IAuditWriter audit)
    {
        if (!authorizer.Has(context, "git.repository.create"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try { var repository = service.Create(request.Name); audit.Write("git", "git.repository.created", repository.Id.ToString()); return Results.Created($"/api/git/repositories/{repository.Id}", repository); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }

    private static async Task<IResult> Push(Guid id, PushRequest request, HttpContext context, GitRepositoryService service, PermissionAuthorizer authorizer, IAuditWriter audit, CancellationToken token)
    {
        if (!authorizer.Has(context, "git.repository.push"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        var commit = await service.PushAsync(id, request.Branch, request.Message, "Maya Chen", token);
        if (commit is null)
        {
            return Results.NotFound();
        }

        audit.Write("git", "git.push.received", id.ToString(), new { request.Branch, commit.Sha }); return Results.Ok(commit);
    }
}

public sealed record CreateRepositoryRequest(string Name);
public sealed record PushRequest(string Branch, string Message);
