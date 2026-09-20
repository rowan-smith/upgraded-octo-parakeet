using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Contracts.Audit;
using Platform.Core.SourceControl;

namespace Platform.Core.Api;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/source/repositories");
        group.MapGet("/", (SourceRepositoryService service) => Results.Ok(service.List()));
        group.MapPost("/", Connect);
        group.MapGet("/{id:guid}/branches", async (Guid id, SourceRepositoryService service, CancellationToken token) => Results.Ok(await service.BranchesAsync(id, token)));
        group.MapGet("/{id:guid}/commits", async (Guid id, string? branch, SourceRepositoryService service, CancellationToken token) => Results.Ok(await service.CommitsAsync(id, branch, token)));
        group.MapGet("/{id:guid}/tree", async (Guid id, string? reference, string? path, SourceRepositoryService service, CancellationToken token) => Results.Ok(await service.TreeAsync(id, reference, path, token)));
        group.MapGet("/{id:guid}/file", async (Guid id, string path, string? reference, SourceRepositoryService service, CancellationToken token) => Results.Ok(await service.FileAsync(id, reference, path, token)));
    }

    private static async Task<IResult> Connect(ConnectSourceRequest request, SourceRepositoryService service, IAuditWriter audit, CancellationToken token)
    {
        var connection = await service.ConnectAsync(request.ProviderId, request.Url, token);
        audit.Write("core", "core.source.connected", connection.Id.ToString(), new { connection.RepositoryId.Provider, connection.RepositoryId.Owner, connection.RepositoryId.Name });
        return Results.Ok(connection);
    }
}

public sealed record ConnectSourceRequest(string Url, string ProviderId = "github");
