using ForgeDeck.Build.Api;
using ForgeDeck.Build.Application;
using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Build.Infrastructure;
using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Search;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Messaging;
using ForgeDeck.Review.Contracts.Events;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Build;

public sealed class BuildModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "build", "Build", "0.2.0", "Community", ["Pipelines.BasicExecution"],
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
        Publishes: BuildEventContracts.All().Select(c => c.Type)
            .Append(CheckUpdatedEventContract.Type)
            .ToArray(),
        Subscribes:
        [
            ReviewEventContracts.Requested.Type,
            ReviewEventContracts.RevisionUpdated.Type,
            GitEventContracts.RepositoryPush.Type,
            BuildEventContracts.PipelineRunRequested.Type
        ],
        Provides: ["build", "check-provider", "build.execution"],
        ExtensionPointContributions:
        [
            ExtensionPoints.BuildSteps,
            ExtensionPoints.BuildRunners,
            ExtensionPoints.ReviewChecks,
            ExtensionPoints.PlatformNavigation
        ],
        ApiRoutePrefixes: ["/api/pipelines"]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PipelinesOptions>(configuration.GetSection(PipelinesOptions.SectionName));
        var connectionString = configuration.GetConnectionString("Build")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<BuildDbContext>(options =>
            DatabaseProvider.Configure(options, configuration, connectionString));
        services.AddSingleton<IPipelineStore, EfPipelineStore>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<BuildDomainService>();
        services.AddSingleton<IBuildService>(sp => sp.GetRequiredService<BuildDomainService>());
        services.AddSingleton<RunnerService>();
        services.AddSingleton<JobExecutionService>();
        services.AddSingleton<ICheckProvider, PipelineCheckProvider>();
        services.AddSingleton<ISearchContributor, PipelineSearchContributor>();
        services.AddSingleton(new EventContractRegistration(BuildEventContracts.All().ToArray()));
        services.AddEventHandler<ReviewRequestedEvent, ReviewRequestedPipelineTrigger>("forgedeck.build.review-requested");
        services.AddEventHandler<ReviewRevisionUpdatedEvent, ReviewRevisionUpdatedPipelineTrigger>("forgedeck.build.review-revision-updated");
        services.AddEventHandler<GitRepositoryPushEvent, GitPushPipelineTrigger>("forgedeck.build.git-push");
        services.AddEventHandler<BuildPipelineRunRequestedEvent, BuildPipelineRunRequestHandler>("forgedeck.build.pipeline-run-requested");
        services.AddHostedService<SimulatedRunnerHostedService>();
        services.AddHostedService<RunnerHeartbeatMonitor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPipelineEndpoints();
}
