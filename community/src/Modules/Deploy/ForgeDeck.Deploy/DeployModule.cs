using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Deploy.Api;
using ForgeDeck.Deploy.Application;
using ForgeDeck.Deploy.Contracts.Events;
using ForgeDeck.Deploy.Infrastructure;
using ForgeDeck.Messaging;
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
        ],
        ApiRoutePrefixes: ["/api/deploy"]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DeployOptions>(configuration.GetSection(DeployOptions.SectionName));
        var connectionString = configuration.GetConnectionString("Deploy")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<DeployDbContext>(options =>
            DatabaseProvider.Configure(options, configuration, connectionString));
        services.AddSingleton<IDeployStore, EfDeployStore>();

        var mode = configuration[$"{DeployOptions.SectionName}:ExecutionMode"] ?? "Immediate";
        if (string.Equals(mode, "Agent", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDeploymentExecutor, QueuedDeploymentExecutor>();
        }
        else
        {
            services.AddSingleton<IDeploymentExecutor, ImmediateDeploymentExecutor>();
        }

        services.AddSingleton<DeployService>();
        services.AddSingleton<DeployAgentService>();
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
