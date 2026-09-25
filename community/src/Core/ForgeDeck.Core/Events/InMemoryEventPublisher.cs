using ForgeDeck.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeDeck.Core.Events;

public sealed class InMemoryEventPublisher(IServiceProvider services) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
    }
}
