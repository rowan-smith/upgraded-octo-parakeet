using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Bus;
using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Messaging.Dispatch;

/// <summary>
/// Re-dispatches events whose consumer deliveries are scheduled for retry.
/// </summary>
public sealed class DeliveryRetryHostedService(
    IEventInbox inbox,
    IEventOutbox outbox,
    IEventSerializer serializer,
    IEventBus bus,
    IOptions<EventRetryOptions> retryOptions,
    IOptions<OutboxDispatcherOptions> dispatcherOptions,
    ILogger<DeliveryRetryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!dispatcherOptions.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var failed = await inbox.ListFailedAsync(50, stoppingToken);
                var due = failed.Where(f =>
                    f.State == DeliveryState.Failed
                    && f.NextAttemptAt is { } next
                    && next <= DateTimeOffset.UtcNow
                    && f.Attempts < retryOptions.Value.MaxAttempts).ToArray();

                foreach (var delivery in due)
                {
                    var record = await outbox.GetAsync(delivery.EventId, stoppingToken);
                    if (record is null)
                    {
                        continue;
                    }

                    await inbox.ResetForRetryAsync(delivery.EventId, delivery.ConsumerId, stoppingToken);
                    var envelope = serializer.DeserializeEnvelope(record.EnvelopeJson);
                    await bus.DispatchAsync(envelope, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Delivery retry loop error.");
            }

            try
            {
                await Task.Delay(dispatcherOptions.Value.PollIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
