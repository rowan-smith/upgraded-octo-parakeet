using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Deploy.Api;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Deploy.Infrastructure;
using ForgeDeck.Messaging;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
        Publishes: DeployEventContracts.All().Select(c => c.Type).ToArray(),
        Subscribes:
        [
            DeployEventContracts.Release.Type,
            DeployEventContracts.RollbackRequested.Type,
            BuildEventContracts.PipelineRunSucceeded.Type,
            BuildEventContracts.ArtifactProduced.Type
        ],
        Provides: ["deploy", "deployment-provider"],
        ExtensionPointContributions:
        [
            ExtensionPoints.DeployProviders,
            ExtensionPoints.DeployTargets,
            ExtensionPoints.PlatformNavigation
        ]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Deploy")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<DeployDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IDeployStore, EfDeployStore>();
        services.AddSingleton<DeployService>();
        services.AddSingleton<DeployDomainService>();
        services.AddSingleton<IDeployService>(sp => sp.GetRequiredService<DeployDomainService>());
        services.AddSingleton(new EventContractRegistration(DeployEventContracts.All().ToArray()));
        services.AddEventHandler<DeployReleaseEvent, DeployReleaseHandler>("forgedeck.deploy.release");
        services.AddEventHandler<DeployRollbackRequestedEvent, DeployRollbackRequestHandler>("forgedeck.deploy.rollback-requested");
        services.AddEventHandler<BuildPipelineRunSucceededEvent, PipelineSucceededDeployHandler>("forgedeck.deploy.pipeline-succeeded");
        services.AddEventHandler<BuildArtifactProducedEvent, BuildArtifactAvailableHandler>("forgedeck.deploy.artifact-produced");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapDeployEndpoints();
}
