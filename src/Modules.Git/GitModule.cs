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
    // Code browse UI is owned by the SPA (Files / Branches / Commits / Tags).
    // Keep Git hosting APIs; do not advertise conflicting /repositories|/branches|/tags nav routes.
    public ModuleManifest Manifest { get; } = new("git", "Git", "0.1.0", "Community",
        ["Git.RepositoryHosting"],
        [], []);
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
