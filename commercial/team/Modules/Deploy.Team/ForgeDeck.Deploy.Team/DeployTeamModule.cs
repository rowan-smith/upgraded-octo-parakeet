using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Deploy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Deploy.Team;

/// <summary>
/// Team Deploy delta. Unlocks multi-environment and promotion/approval policies.
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
            KnownCapabilities.Deploy.MultiEnvironment,
            KnownCapabilities.Deploy.Approvals,
            KnownCapabilities.Deploy.MultiApproval,
            KnownCapabilities.Deploy.EnvironmentPolicy,
            KnownCapabilities.Deploy.PromotionPolicy
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = CommunityAnchor;
        // Promotion / approval gate implementations land as Team contributions over time.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
