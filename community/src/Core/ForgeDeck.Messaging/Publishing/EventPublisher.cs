using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Serialization;

namespace ForgeDeck.Messaging.Publishing;

public sealed class EventPublisher(
    IEventRegistry registry,
    IEventSerializer serializer,
    IEventOutbox outbox,
    IEventBus bus) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent data, PublishOptions? options = null, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new PublishOptions();

        var contract = registry.TryGetByClrType(typeof(TEvent))
                       ?? throw new InvalidOperationException(
                           $"No event contract registered for {typeof(TEvent).Name}. Register it during module activation.");

        var id = Guid.NewGuid();
        var parent = CorrelationContext.Current;
        var correlationId = parent?.CorrelationId ?? id;
        var causationId = parent?.Id;

        var payload = serializer.SerializePayload(data);
        var envelope = new EventEnvelope(
            id,
            contract.Type,
            contract.Version,
            DateTimeOffset.UtcNow,
            options.Actor,
            correlationId,
            causationId,
            options.OrganisationId,
            options.ProjectId,
            options.Publisher,
            payload);

        var payloadJson = payload.GetRawText();

        if (options.Durable)
        {
            await outbox.EnqueueAsync(envelope, payloadJson, cancellationToken);
            return;
        }

        await bus.DispatchAsync(envelope, cancellationToken);
    }
}
