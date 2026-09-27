using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Serialization;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Messaging.Bus;

public sealed class EventRetryOptions
{
    public const string SectionName = "Events:Retry";
    public int MaxAttempts { get; set; } = 5;
    public int InitialBackoffMilliseconds { get; set; } = 500;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int MaxBackoffMilliseconds { get; set; } = 60_000;
}

public sealed class EventBus(
    IEventRegistry registry,
    IEventSerializer serializer,
    EventSubscriber subscriber,
    IEventInbox inbox,
    IOptions<EventRetryOptions> retryOptions,
    ILogger<EventBus> logger) : IEventBus
{
    public async Task DispatchAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var contract = registry.TryGet(envelope.Type, envelope.Version);
        if (contract is null)
        {
            logger.LogDebug("No contract for {Type} v{Version}; treating as no-op deliverable.", envelope.Type, envelope.Version);
            return;
        }

        var subscriptions = subscriber.GetActiveFor(contract.ClrType);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var tasks = subscriptions.Select(sub => DeliverToConsumerAsync(envelope, contract, sub, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task DeliverToConsumerAsync(
        EventEnvelope envelope,
        IEventContract contract,
        EventSubscriber.SubscriptionEntry subscription,
        CancellationToken cancellationToken)
    {
        await inbox.EnsureDeliveryRowAsync(envelope, subscription.ConsumerId, cancellationToken);
        if (!await inbox.TryBeginAsync(envelope.Id, subscription.ConsumerId, cancellationToken))
        {
            return;
        }

        var attempt = 1;
        try
        {
            var payload = serializer.DeserializePayload(contract.ClrType, envelope.Data);
            var typedEnvelope = CreateTypedEnvelope(envelope, payload, contract.ClrType);
            using (CorrelationContext.Push(envelope))
            {
                await InvokeHandlerAsync(subscription.Handler, typedEnvelope, contract.ClrType, cancellationToken);
            }

            await inbox.MarkProcessedAsync(envelope.Id, subscription.ConsumerId, cancellationToken);
        }
        catch (Exception ex)
        {
            var options = retryOptions.Value;
            var delivery = await inbox.GetAsync(envelope.Id, subscription.ConsumerId, cancellationToken);
            attempt = delivery?.Attempts ?? 1;
            var dead = attempt >= options.MaxAttempts;
            DateTimeOffset? next = dead
                ? null
                : DateTimeOffset.UtcNow.AddMilliseconds(ComputeBackoff(options, attempt));
            await inbox.MarkFailedAsync(envelope.Id, subscription.ConsumerId, ex.ToString(), attempt, next, dead, cancellationToken);
            logger.LogWarning(ex,
                "Event {EventId} delivery to {Consumer} failed (attempt {Attempt}, deadLetter={Dead}).",
                envelope.Id, subscription.ConsumerId, attempt, dead);
        }
    }

    private static double ComputeBackoff(EventRetryOptions options, int attempt)
    {
        var ms = options.InitialBackoffMilliseconds * Math.Pow(options.BackoffMultiplier, Math.Max(0, attempt - 1));
        return Math.Min(ms, options.MaxBackoffMilliseconds);
    }

    private static object CreateTypedEnvelope(EventEnvelope envelope, object payload, Type clrType)
    {
        var typed = typeof(EventEnvelope<>).MakeGenericType(clrType);
        return Activator.CreateInstance(
            typed,
            envelope.Id,
            envelope.Type,
            envelope.Version,
            envelope.OccurredAt,
            envelope.Actor,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OrganisationId,
            envelope.ProjectId,
            envelope.Publisher,
            payload)!;
    }

    private static async Task InvokeHandlerAsync(object handler, object typedEnvelope, Type clrType, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(clrType);
        if (!handlerType.IsInstanceOfType(handler))
        {
            throw new InvalidOperationException($"Handler {handler.GetType().Name} does not implement IEventHandler<{clrType.Name}>.");
        }

        var method = handlerType.GetMethod(nameof(IEventHandler<object>.HandleAsync))
                     ?? throw new InvalidOperationException("HandleAsync not found.");
        var task = (Task)method.Invoke(handler, [typedEnvelope, cancellationToken])!;
        await task;
    }
}
