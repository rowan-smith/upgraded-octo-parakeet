using ForgeDeck.Build.Team;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Build.Enterprise;

/// <summary>
/// Enterprise Build delta. Declares runner groups, compliance, approvals, audit, attestation.
/// Concurrent / Team capabilities live in Build.Team.
/// </summary>
public sealed class BuildEnterpriseModule : IPlatformModule
{
    private static readonly Type TeamAnchor = typeof(BuildTeamModule);

    public ModuleManifest Manifest { get; } = new(
        "build-enterprise",
        "Build Enterprise",
        "0.1.0",
        "Enterprise",
        [
            KnownCapabilities.Build.RunnerGroups,
            KnownCapabilities.Build.Compliance,
            KnownCapabilities.Build.Approvals,
            KnownCapabilities.Build.Audit,
            KnownCapabilities.Build.Attestation
        ],
        [],
        []);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = TeamAnchor;
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
