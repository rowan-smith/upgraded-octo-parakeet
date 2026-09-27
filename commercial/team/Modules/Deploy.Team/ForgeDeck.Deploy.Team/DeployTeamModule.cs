using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Deploy.Team;

/// <summary>
/// Team Deploy delta. Declares MultiEnvironment only (soft-limit unlock).
/// Community enforces the 1-environment soft limit when Deploy.MultiEnvironment is absent.
/// </summary>
public sealed class DeployTeamModule : IPlatformModule
{
    private static readonly Type CommunityAnchor = typeof(DeployModule);

    public ModuleManifest Manifest { get; } = new(
        "deploy-team",
        "Deploy Team",
        "0.1.0",
        "Team",
        [
            KnownCapabilities.Deploy.MultiEnvironment
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = CommunityAnchor;
        // Multi-environment unlock is capability-gated in Community DeployService.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
