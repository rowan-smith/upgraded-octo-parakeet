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
    public void EnsureCreated()
    {
        using var db = dbFactory.CreateDbContext();
        db.Database.EnsureCreated();
        if (registry.TryGet(CheckUpdatedEventContract.Type, CheckUpdatedEventContract.Version) is null)
        {
            registry.Register(CheckUpdatedEventContract.Create());
        }
    }
}
