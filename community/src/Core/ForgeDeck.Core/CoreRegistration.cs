using ForgeDeck.Contracts.Audit;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Integrations;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Contracts.Search;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Audit;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Events;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Identity;
using ForgeDeck.Core.Integrations;
using ForgeDeck.Core.Licensing;
using ForgeDeck.Core.Modules;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.Search;
using ForgeDeck.Core.SourceControl;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Core;

public static class CoreRegistration
{
    public static IServiceCollection AddPlatformCore(this IServiceCollection services, IReadOnlyList<IPlatformModule> modules, IConfiguration configuration)
    {
        services.AddSingleton<AuditStore>();
        services.AddSingleton<IEnumerable<IPlatformModule>>(modules);
        ExtensionLifecycleService.RememberLoaded(modules);
        services.Configure<LicensingOptions>(configuration.GetSection(LicensingOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddSingleton<ManagedLicenceEntitlementStore>();
        services.AddSingleton<ILicenceEntitlementStore>(sp => sp.GetRequiredService<ManagedLicenceEntitlementStore>());
        services.AddSingleton<LicenceService>();
        services.AddSingleton<IEntitlementService, EntitlementService>();
        services.AddSingleton<ICapabilityService, CapabilityService>();
        services.AddSingleton<CapabilityAuthorizer>();
        services.AddSingleton<IAuditWriter, AuditWriter>();
        services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
        services.AddSingleton<PermissionAuthorizer>();
        services.AddHttpContextAccessor();
        var connectionString = configuration.GetConnectionString("Platform") ?? "Data Source=data/forgedeck.db";
        var keyPath = Path.GetFullPath(configuration["Data:ProtectionKeysPath"] ?? "data/keys");
        Directory.CreateDirectory(keyPath);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("ForgeDeck");
        services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(connectionString));
        services.AddSingleton<CoreSchemaInitializer>();
        services.AddSingleton<ITenancyStore, SqliteTenancyStore>();
        services.AddSingleton<IExtensionRegistry, SqliteExtensionRegistry>();
        services.AddSingleton<IExtensionPackageVerifier, SignedExtensionPackageVerifier>();
        services.AddSingleton<ExtensionLifecycleService>();
        services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<MembershipService>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<ProjectAccessService>();
        services.AddSingleton<TeamService>();
        services.AddSingleton<InvitationService>();
        services.AddSingleton<RepositoryService>();
        services.AddSingleton<DevelopmentSeedService>();
        services.AddSingleton<PlatformContextStore>();
        services.AddSingleton<IProviderCredentialStore, EncryptedProviderCredentialStore>();
        services.AddSingleton<ISourceConnectionStore, SqliteSourceConnectionStore>();
        services.AddSingleton<ILocalRepositoryStore, SqliteLocalRepositoryStore>();
        services.AddSingleton<LocalRepositoryInspector>();
        services.AddSingleton<LocalRepositoryService>();
        services.AddSingleton<SourceProviderRegistry>();
        services.AddSingleton<SourceRepositoryService>();
        services.AddSingleton<SourceConnectionSeeder>();
        services.AddSingleton<IProviderCatalogue, ProviderCatalogue>();
        services.AddSingleton<IModuleContext, ModuleContext>();
        services.AddSingleton<InProcessModuleHost>();
        services.AddSingleton<OutOfProcessModuleHost>();
        services.AddSingleton<IModuleHost, CompositeModuleHost>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<RoleService>();
        services.AddSingleton<EffectivePermissionService>();
        return services;
    }

    public static WebApplication UseMvpAuthentication(this WebApplication app)
    {
        app.UseMiddleware<MvpAuthenticationMiddleware>();
        return app;
    }
}
