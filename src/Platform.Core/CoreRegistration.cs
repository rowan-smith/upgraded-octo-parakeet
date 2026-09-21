using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Contracts.Audit;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Events;
using Platform.Contracts.Integrations;
using Platform.Contracts.Licensing;
using Platform.Contracts.Modules;
using Platform.Contracts.SourceControl;
using Platform.Core.Audit;
using Platform.Core.Application;
using Platform.Core.Capabilities;
using Platform.Core.Context;
using Platform.Core.Events;
using Platform.Core.Identity;
using Platform.Core.Integrations;
using Platform.Core.Licensing;
using Platform.Core.Persistence;
using Platform.Core.SourceControl;

namespace Platform.Core;

public static class CoreRegistration
{
    public static IServiceCollection AddPlatformCore(this IServiceCollection services, IReadOnlyList<IPlatformModule> modules, IConfiguration configuration)
    {
        services.AddSingleton<AuditStore>();
        services.AddSingleton<IEnumerable<IPlatformModule>>(modules);
        services.Configure<LicensingOptions>(configuration.GetSection(LicensingOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        if (configuration.GetSection(BootstrapOptions.SectionName).Exists() == false)
        {
            // Defaults: admin/admin with development warning.
        }
        services.AddSingleton<ManagedLicenceEntitlementStore>();
        services.AddSingleton<ILicenceEntitlementStore>(sp => sp.GetRequiredService<ManagedLicenceEntitlementStore>());
        services.AddSingleton<LicenceService>();
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
        services.AddSingleton<IPasswordHasher<Platform.Core.Domain.UserAccount>, PasswordHasher<Platform.Core.Domain.UserAccount>>();
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
        return services;
    }

    public static WebApplication UseMvpAuthentication(this WebApplication app)
    {
        app.UseMiddleware<MvpAuthenticationMiddleware>();
        return app;
    }
}
