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
        "pipelines", "Pipelines", "0.1.0", "Community", ["Pipelines.BasicExecution"],
        [new("pipelines", "Pipelines", "/pipelines", "Automation", 10), new("runs", "Runs", "/runs", "Automation", 20), new("runners", "Runners", "/runners", "Automation", 30)],
        [new("change", "checks", "Checks", "/api/review/changes/{id}/checks", 50)]);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPipelineRepository, InMemoryPipelineRepository>();
        services.AddSingleton<IPipelineRunner, DeterministicPipelineRunner>();
        services.AddSingleton<PipelineService>();
        services.AddSingleton<ICheckProvider, PipelineCheckProvider>();
        services.AddSingleton<IEventHandler<ChangeOpened>, ChangeOpenedPipelineTrigger>();
        services.AddSingleton<IEventHandler<PushReceived>, PushReceivedPipelineTrigger>();
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPipelineEndpoints();
}
