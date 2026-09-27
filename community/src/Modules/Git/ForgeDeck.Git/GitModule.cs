using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Git.Api;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Git.Infrastructure;
using ForgeDeck.Messaging;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
        Publishes:
        [
            GitEventContracts.RepositoryCreated.Type,
            GitEventContracts.RepositoryPush.Type,
            GitEventContracts.RepositoryRefUpdated.Type,
            GitEventContracts.RepositoryDeleted.Type
        ],
        Provides: ["git", "source-provider"],
        ExtensionPointContributions: [ExtensionPoints.PlatformNavigation]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Git")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<GitDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IGitRepositoryStore, EfGitRepositoryStore>();
        services.AddSingleton<GitRepositoryService>();
        services.AddSingleton<GitDomainService>();
        services.AddSingleton<IGitService>(sp => sp.GetRequiredService<GitDomainService>());
        services.AddSingleton<NativeGitSourceProvider>();
        services.AddSingleton<ISourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton<IChangeSourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton(new EventContractRegistration(GitEventContracts.All().ToArray()));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapGitEndpoints();
}
