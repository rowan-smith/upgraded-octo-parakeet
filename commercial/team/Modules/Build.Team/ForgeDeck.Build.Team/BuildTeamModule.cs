using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Build.Team;

/// <summary>
/// Team Build delta. Declares Concurrent only (soft-limit unlock).
/// Community already enforces the concurrent soft limit when Build.Concurrent is absent.
/// </summary>
public sealed class BuildTeamModule : IPlatformModule
{
    private static readonly Type CommunityAnchor = typeof(BuildModule);

    public ModuleManifest Manifest { get; } = new(
        "build-team",
        "Build Team",
        "0.1.0",
        "Team",
        [
            KnownCapabilities.Build.Concurrent
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = CommunityAnchor;
        // Concurrent unlock is capability-gated in Community PipelineService.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
