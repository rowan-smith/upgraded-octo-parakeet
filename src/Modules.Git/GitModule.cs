using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Git.Api;
using Modules.Git.Application;
using Modules.Git.Infrastructure;
using Platform.Contracts.Modules;
using Platform.Contracts.SourceControl;

namespace Modules.Git;

public sealed class GitModule : IPlatformModule
{
    public ModuleManifest Manifest { get; } = new("git", "Git", "0.1.0", "Community",
        ["Git.RepositoryHosting"],
        [new("repositories", "Repositories", "/repositories", "Code", 10), new("branches", "Branches", "/branches", "Code", 20), new("tags", "Tags", "/tags", "Code", 30)], []);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IGitRepositoryStore, InMemoryGitRepositoryStore>();
        services.AddSingleton<GitRepositoryService>();
        services.AddSingleton<NativeGitSourceProvider>();
        services.AddSingleton<ISourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton<IChangeSourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapGitEndpoints();
}
