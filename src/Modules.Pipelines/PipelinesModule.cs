using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Pipelines.Api;
using Modules.Pipelines.Application;
using Modules.Pipelines.Infrastructure;
using Platform.Contracts.Checks;
using Platform.Contracts.Events;
using Platform.Contracts.Modules;

namespace Modules.Pipelines;

public sealed class PipelinesModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new(
        "pipelines", "Pipelines", "0.2.0", "Community", ["Pipelines.BasicExecution"],
        [
            new("pipelines", "Pipelines", "/pipelines", "Automation", 10),
            new("runs", "Runs", "/runs", "Automation", 20),
            new("runners", "Runners", "/runners", "Automation", 30)
        ],
        [new("change", "checks", "Checks", "/api/review/changes/{id}/checks", 50)]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PipelinesOptions>(configuration.GetSection(PipelinesOptions.SectionName));
        services.AddSingleton<PipelineSchemaInitializer>();
        services.AddSingleton<IPipelineStore, SqlitePipelineStore>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<RunnerService>();
        services.AddSingleton<JobExecutionService>();
        services.AddSingleton<ICheckProvider, PipelineCheckProvider>();
        services.AddSingleton<Platform.Contracts.Onboarding.IOnboardingContributor, PipelinesOnboardingContributor>();
        services.AddSingleton<IEventHandler<ChangeOpened>, ChangeOpenedPipelineTrigger>();
        services.AddSingleton<IEventHandler<ChangeUpdated>, ChangeUpdatedPipelineTrigger>();
        services.AddSingleton<IEventHandler<PushReceived>, PushReceivedPipelineTrigger>();
        services.AddHostedService<SimulatedRunnerHostedService>();
        services.AddHostedService<RunnerHeartbeatMonitor>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPipelineEndpoints();
}
