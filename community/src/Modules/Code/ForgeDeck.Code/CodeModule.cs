using ForgeDeck.Contracts.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Code;

public sealed class CodeModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "code", "Code", "1.0.0", "Community", ["source-browser"],
        [
            new("files", "Files", "/files", "Code", 110),
            new("branches", "Branches", "/source-branches", "Code", 120),
            new("commits", "Commits", "/commits", "Code", 130),
            new("tags", "Tags", "/tags", "Code", 140)
        ],
        [],
        Permissions: [PlatformPermissions.ProjectRead, PlatformPermissions.CodeRead, PlatformPermissions.RepositoryRead],
        Provides: ["code", "source-browser"],
        ExtensionPointContributions: [ExtensionPoints.PlatformNavigation],
        ApiRoutePrefixes: ["/api/code"]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Thin module: browse UI is SPA-owned; source data comes from connected providers.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/code");
        group.MapGet("/status", () => Results.Ok(new { module = "code", requiresSourceProvider = true }));
    }
}
