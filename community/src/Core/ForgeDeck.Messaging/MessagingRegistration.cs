using ForgeDeck.Contracts.Checks;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Bus;
using ForgeDeck.Messaging.Diagnostics;
using ForgeDeck.Messaging.Dispatch;
using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Persistence;
using ForgeDeck.Messaging.Publishing;
using ForgeDeck.Messaging.Registry;
using ForgeDeck.Messaging.Serialization;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Messaging;

public static class MessagingRegistration
{
    public static IServiceCollection AddForgeDeckMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.Configure<EventRetryOptions>(configuration.GetSection(EventRetryOptions.SectionName));
        services.Configure<OutboxDispatcherOptions>(configuration.GetSection(OutboxDispatcherOptions.SectionName));

        services.AddDbContextFactory<MessagingDbContext>(o => o.UseSqlite(connectionString));
        services.AddSingleton<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<EventRegistry>();
        services.AddSingleton<IEventRegistry>(sp => sp.GetRequiredService<EventRegistry>());
        services.AddSingleton<EventSubscriber>();
        services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<EventSubscriber>());
        services.AddSingleton<IEventOutbox, EfEventOutbox>();
        services.AddSingleton<IEventInbox, EfEventInbox>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IEventPublisher, EventPublisher>();
        services.AddSingleton<IEventDiagnostics, EventDiagnosticsService>();
        services.AddSingleton<IModuleEventLifecycle, ModuleEventLifecycle>();
        services.AddHostedService<OutboxDispatcherHostedService>();
        services.AddHostedService<DeliveryRetryHostedService>();

        services.AddSingleton<IMessagingBootstrapper, MessagingBootstrapper>();
        return services;
    }

    public static void EnsureMessagingSchema(this IServiceProvider services)
    {
        services.GetRequiredService<IMessagingBootstrapper>().EnsureCreated();
    }
}

public interface IMessagingBootstrapper
{
    void EnsureCreated();
}

public sealed class MessagingBootstrapper(
    IDbContextFactory<MessagingDbContext> dbFactory,
    IEventRegistry registry) : IMessagingBootstrapper
{
    private static readonly object SchemaGate = new();

    public void EnsureCreated()
    {
        using var db = dbFactory.CreateDbContext();
        // PlatformDbContext.EnsureCreated may have already created the SQLite file.
        // EnsureCreated() then no-ops; create messaging tables explicitly when missing.
        db.Database.OpenConnection();
        lock (SchemaGate)
        {
            var conn = db.Database.GetDbConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='core_event_outbox'";
                var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                if (!exists)
                {
                    try
                    {
                        var creator = (IRelationalDatabaseCreator)db.Database.GetService(typeof(IRelationalDatabaseCreator))!;
                        creator.CreateTables();
                    }
                    catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                        || (ex.InnerException?.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        // Concurrent bootstrap.
                    }
                }
            }
        }

        if (registry.TryGet(CheckUpdatedEventContract.Type, CheckUpdatedEventContract.Version) is null)
        {
            registry.Register(CheckUpdatedEventContract.Create());
        }
    }
}
