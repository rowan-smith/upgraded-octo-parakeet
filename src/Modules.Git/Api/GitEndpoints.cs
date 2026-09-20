using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Git.Application;
using Platform.Contracts.Audit;
using Platform.Core.Identity;

namespace Modules.Git.Api;

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
        group.MapPost("/repositories", CreateRepository);
        group.MapPost("/repositories/{id:guid}/push", Push);
    }
    private static IResult CreateRepository(CreateRepositoryRequest request, HttpContext context, GitRepositoryService service, PermissionAuthorizer authorizer, IAuditWriter audit)
    {
        if (!authorizer.Has(context, "git.repository.create")) return PermissionAuthorizer.Forbidden();
        try { var repository = service.Create(request.Name); audit.Write("git", "git.repository.created", repository.Id.ToString()); return Results.Created($"/api/git/repositories/{repository.Id}", repository); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }
    private static async Task<IResult> Push(Guid id, PushRequest request, HttpContext context, GitRepositoryService service, PermissionAuthorizer authorizer, IAuditWriter audit, CancellationToken token)
    {
        if (!authorizer.Has(context, "git.repository.push")) return PermissionAuthorizer.Forbidden();
        var commit = await service.PushAsync(id, request.Branch, request.Message, "Maya Chen", token);
        if (commit is null) return Results.NotFound(); audit.Write("git", "git.push.received", id.ToString(), new { request.Branch, commit.Sha }); return Results.Ok(commit);
    }
}
