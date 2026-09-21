using Microsoft.Extensions.DependencyInjection;
using Modules.Pipelines.Application;
using Platform.Contracts.Onboarding;
using Platform.Core.Persistence;

namespace Modules.Pipelines;

public sealed class PipelinesOnboardingContributor : IOnboardingContributor
{
    public string Id => "pipelines-setup";
    public string Title => "Configure Pipelines";
    public int Order => 20;
    public bool IsRequired => false;
    public string? RequiredCapability => null;

    public Task<bool> IsCompleteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var store = services.GetRequiredService<ITenancyStore>();
        if (store.ListProjects().Count == 0) return Task.FromResult(false);
        var pipelines = services.GetService<IPipelineStore>();
        if (pipelines is null) return Task.FromResult(false);
        return Task.FromResult(pipelines.ListDefinitions().Count > 0 || pipelines.ListRunners().Count > 0);
    }
}
