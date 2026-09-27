using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Services;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Git.Api;
using ForgeDeck.Git.Application;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Git.Infrastructure;
using ForgeDeck.Messaging;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        ExtensionPointContributions: [ExtensionPoints.PlatformNavigation],
        ApiRoutePrefixes: ["/api/git"]);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Git")
            ?? configuration.GetConnectionString("Platform")
            ?? "Data Source=data/forgedeck.db";
        services.AddDbContextFactory<GitDbContext>(options =>
            DatabaseProvider.Configure(options, configuration, connectionString));
        services.AddSingleton<IGitRepositoryStore, EfGitRepositoryStore>();
        services.AddSingleton<IGitObjectStore>(sp => CreateObjectStore(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        services.AddSingleton<GitRepositoryService>();
        services.AddSingleton<GitSmartHttpTransport>();
        services.AddSingleton<GitDomainService>();
        services.AddSingleton<IGitService>(sp => sp.GetRequiredService<GitDomainService>());
        services.AddSingleton<NativeGitSourceProvider>();
        services.AddSingleton<ISourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton<IChangeSourceProvider>(sp => sp.GetRequiredService<NativeGitSourceProvider>());
        services.AddSingleton(new EventContractRegistration(GitEventContracts.All().ToArray()));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapGitEndpoints();

    internal static IGitObjectStore CreateObjectStore(IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = configuration["Git:ObjectStore"];
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = environment.IsEnvironment("Testing") ? "InMemory" : "FileSystem";
        }

        if (string.Equals(mode, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return new InMemoryGitObjectStore();
        }

        var root = configuration["Git:ObjectStoreRoot"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(environment.ContentRootPath, "data", "git");
        }
        else if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(environment.ContentRootPath, root);
        }

        return new FileSystemGitObjectStore(root);
    }
}
