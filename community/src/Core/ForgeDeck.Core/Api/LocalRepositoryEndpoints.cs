using System.Diagnostics;
using ForgeDeck.Contracts.Audit;
using ForgeDeck.Core.SourceControl;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Core.Api;

public static class LocalRepositoryEndpoints
{
    public static void MapLocalRepositoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/current/local-repository");
        group.MapGet("/", Get);
        group.MapPost("/detect", Detect);
        group.MapPost("/", Associate);
        group.MapDelete("/", Disconnect);
        group.MapPost("/open", Open);
    }

    private static IResult Get(LocalRepositoryService service)
    {
        var association = service.CurrentAssociation();
        if (association is null)
        {
            return Results.Ok(new { associated = false });
        }

        var status = service.CurrentStatus();
        return Results.Ok(new { associated = true, association, status });
    }

    private static IResult Detect(DetectLocalRepositoryRequest request, LocalRepositoryService service)
    {
        try { return Results.Ok(service.Detect(request.Path)); }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = exception.Message, title = "Validation Error" });
        }
    }

    private static async Task<IResult> Associate(AssociateLocalRepositoryRequest request, LocalRepositoryService service, IAuditWriter audit, CancellationToken token)
    {
        try
        {
            var (association, info, connection) = await service.AssociateAsync(request.Path, request.ConnectGitHub, token);
            audit.Write("core", "core.local_repository.associated", association.ProjectId.ToString(), new { association.Root, github = info.DetectedGitHub?.FullName });
            return Results.Ok(new { association, status = info, connection });
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = exception.Message, title = "Validation Error" });
        }
    }

    private static IResult Disconnect(LocalRepositoryService service, IAuditWriter audit)
    {
        var existing = service.CurrentAssociation();
        service.Disconnect();
        if (existing is not null)
        {
            audit.Write("core", "core.local_repository.removed", existing.ProjectId.ToString());
        }

        return Results.NoContent();
    }

    private static IResult Open(OpenLocalRepositoryRequest? request, LocalRepositoryService service, IAuditWriter audit)
    {
        var association = service.CurrentAssociation();
        if (association is null)
        {
            return Results.BadRequest(new { error = "No local repository is associated.", title = "Validation Error" });
        }

        try
        {
            var root = Path.GetFullPath(association.Root);
            var target = Path.GetFullPath(string.IsNullOrWhiteSpace(request?.Path) ? root : request.Path);
            if (!IsUnderRoot(target, root))
            {
                return Results.BadRequest(new { error = "Path must be under the associated local repository root.", title = "Validation Error" });
            }

            if (!Directory.Exists(target))
            {
                return Results.BadRequest(new { error = "Directory was not found.", title = "Validation Error" });
            }

            OpenFolder(target);
            audit.Write("core", "core.local_repository.opened", association.ProjectId.ToString(), new { path = target });
            return Results.Ok(new { opened = true, path = target });
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = exception.Message, title = "Validation Error" });
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenFolder(string path)
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true };
        }
        else if (OperatingSystem.IsMacOS())
        {
            start = new ProcessStartInfo("open", path) { UseShellExecute = false };
        }
        else
        {
            start = new ProcessStartInfo("xdg-open", path) { UseShellExecute = false };
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to open the folder.");
        process.Dispose();
    }
}

public sealed record DetectLocalRepositoryRequest(string Path);
public sealed record AssociateLocalRepositoryRequest(string Path, bool ConnectGitHub = true);
public sealed record OpenLocalRepositoryRequest(string? Path = null);
