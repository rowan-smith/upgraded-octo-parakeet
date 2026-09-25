using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Deploy.Team;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Deploy.Enterprise;

/// <summary>
/// Enterprise Deploy delta. Declares multi-site, change windows, advanced rollback, compliance audit.
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
        [
            KnownCapabilities.Deploy.MultiSite,
            KnownCapabilities.Deploy.AdvancedRollback,
            KnownCapabilities.Deploy.ChangeWindow,
            KnownCapabilities.Deploy.ComplianceAudit
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
