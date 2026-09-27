using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Messaging;

/// <summary>Helpers for modules to register handlers with stable consumer IDs.</summary>
public static class EventSubscriptionExtensions
{
    public static IServiceCollection AddEventHandler<TEvent, THandler>(
        this IServiceCollection services,
        string consumerId)
        where TEvent : class
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddSingleton<THandler>();
        services.AddSingleton(new EventHandlerRegistration(consumerId, typeof(TEvent), typeof(THandler)));
        return services;
    }

    public static void ActivateRegisteredHandlers(this IServiceProvider services)
    {
        var subscriber = services.GetRequiredService<EventSubscriber>();
        var registrations = services.GetServices<EventHandlerRegistration>();
        foreach (var registration in registrations)
        {
            var handler = services.GetRequiredService(registration.HandlerType);
            var method = typeof(EventSubscriber)
                .GetMethod(nameof(EventSubscriber.Subscribe))!
                .MakeGenericMethod(registration.EventType);
            method.Invoke(subscriber, [registration.ConsumerId, handler]);
        }
    }
}

public sealed record EventHandlerRegistration(string ConsumerId, Type EventType, Type HandlerType);

public sealed record EventContractRegistration(IReadOnlyList<IEventContract> Contracts);

