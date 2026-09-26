using ForgeDeck.Build.Api;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Infrastructure;
using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Onboarding;
using ForgeDeck.Contracts.Search;
using ForgeDeck.Contracts.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Build;

public sealed class BuildModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "pipelines", "Build", "0.2.0", "Community", ["Pipelines.BasicExecution"],
        [
            new("pipelines", "Pipelines", "/pipelines", "Build", 310),
            new("runs", "Runs", "/runs", "Build", 320),
            new("jobs", "Jobs", "/jobs", "Build", 325),
            new("tests", "Tests", "/tests", "Build", 326),
            new("artifacts", "Artifacts", "/artifacts", "Build", 327)
        ],
        [new("change", "checks", "Checks", "/api/review/changes/{id}/checks", 50)],
        Permissions:
        [
            PlatformPermissions.ProjectRead,
            PlatformPermissions.RepositoryRead,
            PlatformPermissions.BuildRead,
            PlatformPermissions.BuildTrigger,
            PlatformPermissions.BuildArtifactsRead
        ],
        Publishes: ["build.started", "build.completed", "build.failed", "artifact.published"],
        Subscribes: ["repository.push", "review.created", "review.updated"],
        Provides: ["build", "check-provider", "build.execution"],
        ExtensionPointContributions:
        [
            ExtensionPoints.BuildSteps,
            ExtensionPoints.BuildRunners,
            ExtensionPoints.ReviewChecks,
            ExtensionPoints.PlatformNavigation
        ]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PipelinesOptions>(configuration.GetSection(PipelinesOptions.SectionName));
        services.AddSingleton<PipelineSchemaInitializer>();
        services.AddSingleton<IPipelineStore, SqlitePipelineStore>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<BuildDomainService>();
        services.AddSingleton<IBuildService>(sp => sp.GetRequiredService<BuildDomainService>());
        services.AddSingleton<RunnerService>();
        services.AddSingleton<JobExecutionService>();
        services.AddSingleton<ICheckProvider, PipelineCheckProvider>();
        services.AddSingleton<IOnboardingContributor, PipelinesOnboardingContributor>();
        services.AddSingleton<ISearchContributor, PipelineSearchContributor>();
        services.AddSingleton<IEventHandler<ChangeOpened>, ChangeOpenedPipelineTrigger>();
        services.AddSingleton<IEventHandler<ChangeUpdated>, ChangeUpdatedPipelineTrigger>();
        services.AddSingleton<IEventHandler<PushReceived>, PushReceivedPipelineTrigger>();
        services.AddHostedService<SimulatedRunnerHostedService>();
        services.AddHostedService<RunnerHeartbeatMonitor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPipelineEndpoints();
}
