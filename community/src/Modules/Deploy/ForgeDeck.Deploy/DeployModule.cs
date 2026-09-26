using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Deploy.Api;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Deploy;

public sealed class DeployModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "deploy", "Deploy", "0.1.0", "Community", ["Deploy.Basic"],
        [
            new("environments", "Environments", "/environments", "Deploy", 410),
            new("deployments", "Deployments", "/deployments", "Deploy", 420)
        ],
        [],
        Permissions:
        [
            PlatformPermissions.ProjectRead,
            PlatformPermissions.DeployRead,
            PlatformPermissions.DeployExecute
        ],
        Publishes: ["deploy.started", "deploy.completed", "deploy.failed", "deploy.rolledback"],
        Subscribes: ["build.completed", "artifact.published"],
        Provides: ["deploy", "deployment-provider"],
        ExtensionPointContributions:
        [
            ExtensionPoints.DeployProviders,
            ExtensionPoints.DeployTargets,
            ExtensionPoints.PlatformNavigation
        ]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DeploySchemaInitializer>();
        services.AddSingleton<IDeployStore, SqliteDeployStore>();
        services.AddSingleton<DeployService>();
        services.AddSingleton<DeployDomainService>();
        services.AddSingleton<IDeployService>(sp => sp.GetRequiredService<DeployDomainService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapDeployEndpoints();
}
