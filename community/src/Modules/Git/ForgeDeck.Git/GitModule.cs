using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Git.Api;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Git;

public sealed class GitModule : IPlatformModule
{
    // Code browse UI is owned by the SPA (Files / Branches / Commits / Tags).
    // Keep Git hosting APIs; do not advertise conflicting /repositories|/branches|/tags nav routes.
    public ModuleManifest Manifest { get; } = new("git", "Git", "0.1.0", "Community",
        ["Git.RepositoryHosting"],
        [], [],
        Permissions: [PlatformPermissions.RepositoryRead, PlatformPermissions.RepositoryWrite, PlatformPermissions.ProjectRead],
        Publishes: ["repository.created", "repository.push", "repository.tag.created"],
        Provides: ["git", "source-provider"],
        ExtensionPointContributions: [ExtensionPoints.PlatformNavigation]);
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IGitRepositoryStore, InMemoryGitRepositoryStore>();
        services.AddSingleton<GitRepositoryService>();
        services.AddSingleton<GitDomainService>();
        services.AddSingleton<IGitService>(sp => sp.GetRequiredService<GitDomainService>());
        services.AddSingleton<NativeGitSourceProvider>();
        services.AddSingleton<ISourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton<IChangeSourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapGitEndpoints();
}
