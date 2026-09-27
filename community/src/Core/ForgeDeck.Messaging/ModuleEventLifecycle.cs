using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Messaging;

/// <summary>
/// Enables/disables event consumers for a module runtime without replaying history.
/// </summary>
public interface IModuleEventLifecycle
{
    void DeactivateModule(string runtimeId);
    void ReactivateModule(string runtimeId, IServiceProvider services);
}

public sealed class ModuleEventLifecycle(
    EventSubscriber subscriber,
    IEventInbox inbox) : IModuleEventLifecycle
{
    public void DeactivateModule(string runtimeId)
    {
        var prefix = $"forgedeck.{Normalize(runtimeId)}.";
        // Build uses runtime id "pipelines" but consumer ids use "forgedeck.build."
        var aliases = runtimeId.Equals("pipelines", StringComparison.OrdinalIgnoreCase)
            ? new[] { "forgedeck.build.", prefix }
            : new[] { prefix };

        foreach (var sub in subscriber.ListSubscriptions())
        {
            if (aliases.Any(a => sub.ConsumerId.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            {
                subscriber.Unsubscribe(sub.ConsumerId);
                inbox.CancelConsumerAsync(sub.ConsumerId).GetAwaiter().GetResult();
            }
        }
    }

    public void ReactivateModule(string runtimeId, IServiceProvider services)
    {
        // Re-activate all registered handlers whose consumer id belongs to this module.
        var prefix = $"forgedeck.{Normalize(runtimeId)}.";
        var aliases = runtimeId.Equals("pipelines", StringComparison.OrdinalIgnoreCase)
            ? new[] { "forgedeck.build.", prefix }
            : new[] { prefix };

        var registrations = services.GetServices<EventHandlerRegistration>();
        foreach (var registration in registrations)
        {
            if (!aliases.Any(a => registration.ConsumerId.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var handler = services.GetRequiredService(registration.HandlerType);
            var method = typeof(EventSubscriber)
                .GetMethod(nameof(EventSubscriber.Subscribe))!
                .MakeGenericMethod(registration.EventType);
            method.Invoke(subscriber, [registration.ConsumerId, handler]);
        }
    }

    private static string Normalize(string runtimeId) =>
        runtimeId.Equals("pipelines", StringComparison.OrdinalIgnoreCase) ? "build" : runtimeId.ToLowerInvariant();
}
