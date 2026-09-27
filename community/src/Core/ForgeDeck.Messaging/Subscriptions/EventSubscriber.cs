using System.Collections.Concurrent;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Messaging.Subscriptions;

public sealed class EventSubscriber : IEventSubscriber
{
    private readonly ConcurrentDictionary<string, SubscriptionEntry> _byConsumer = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Subscribe<TEvent>(string consumerId, IEventHandler<TEvent> handler) where TEvent : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new SubscriptionEntry(consumerId, typeof(TEvent), handler, Active: true);
        lock (_gate)
        {
            _byConsumer[consumerId] = entry;
        }
    }

    public void Unsubscribe(string consumerId)
    {
        lock (_gate)
        {
            if (_byConsumer.TryGetValue(consumerId, out var existing))
            {
                _byConsumer[consumerId] = existing with { Active = false };
            }
        }
    }

    public void Remove(string consumerId)
    {
        lock (_gate)
        {
            _byConsumer.TryRemove(consumerId, out _);
        }
    }

    public IReadOnlyList<EventSubscriptionInfo> ListSubscriptions()
    {
        lock (_gate)
        {
            return _byConsumer.Values
                .Select(e => new EventSubscriptionInfo(
                    e.ConsumerId,
                    e.EventClrType.Name,
                    null,
                    e.Handler.GetType(),
                    e.Active))
                .OrderBy(s => s.ConsumerId)
                .ToArray();
        }
    }

    public IReadOnlyList<SubscriptionEntry> GetActiveFor(Type eventClrType)
    {
        lock (_gate)
        {
            return _byConsumer.Values
                .Where(e => e.Active && e.EventClrType == eventClrType)
                .ToArray();
        }
    }

    public IReadOnlyList<SubscriptionEntry> GetAllActive()
    {
        lock (_gate)
        {
            return _byConsumer.Values.Where(e => e.Active).ToArray();
        }
    }

    public sealed record SubscriptionEntry(string ConsumerId, Type EventClrType, object Handler, bool Active);
}
