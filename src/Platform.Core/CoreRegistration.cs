using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Platform.Contracts.Audit;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Events;
using Platform.Contracts.Integrations;
using Platform.Contracts.Modules;
using Platform.Core.Audit;
using Platform.Core.Capabilities;
using Platform.Core.Context;
using Platform.Core.Events;
using Platform.Core.Identity;
using Platform.Core.Integrations;
using Platform.Core.Persistence;
using Platform.Core.SourceControl;
using Platform.Contracts.SourceControl;
using Microsoft.Extensions.Configuration;

namespace Platform.Core;

public static class CoreRegistration
{
    public static IServiceCollection AddPlatformCore(this IServiceCollection services, IReadOnlyList<IPlatformModule> modules, IConfiguration configuration)
    {
        services.AddSingleton<PlatformContextStore>();
        services.AddSingleton<AuditStore>();
        services.AddSingleton<IEnumerable<IPlatformModule>>(modules);
        services.AddSingleton<ICapabilityService, CapabilityService>();
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
        services.AddSingleton<IProviderCredentialStore, EncryptedProviderCredentialStore>();
        services.AddSingleton<ISourceConnectionStore, SqliteSourceConnectionStore>();
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
