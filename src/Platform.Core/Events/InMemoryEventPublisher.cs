using Microsoft.Extensions.DependencyInjection;
using Platform.Contracts.Events;

namespace Platform.Core.Events;

public sealed class InMemoryEventPublisher(IServiceProvider services) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, cancellationToken);
    }
}
