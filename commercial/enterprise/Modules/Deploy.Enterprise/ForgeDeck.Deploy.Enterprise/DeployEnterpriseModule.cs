using ForgeDeck.Contracts.Modules;
using ForgeDeck.Deploy.Team;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Deploy.Enterprise;

/// <summary>
/// Enterprise Deploy delta. Enterprise capabilities are planned; none declared yet.
/// Multi-environment lives in Deploy.Team.
/// </summary>
public sealed class DeployEnterpriseModule : IPlatformModule
{
    private static readonly Type TeamAnchor = typeof(DeployTeamModule);

    public ModuleManifest Manifest { get; } = new(
        "deploy-enterprise",
        "Deploy Enterprise",
        "0.1.0",
        "Enterprise",
        [],
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
