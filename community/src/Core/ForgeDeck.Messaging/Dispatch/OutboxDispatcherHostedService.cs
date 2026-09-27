using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Messaging.Dispatch;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Events:Dispatcher";
    public int PollIntervalMilliseconds { get; set; } = 250;
    public int BatchSize { get; set; } = 32;
    public bool Enabled { get; set; } = true;
}

public sealed class OutboxDispatcherHostedService(
    IEventOutbox outbox,
    IEventSerializer serializer,
    IEventBus bus,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    public async Task DispatchPendingOnceAsync(CancellationToken cancellationToken = default)
    {
        var batch = await outbox.ClaimPendingAsync(options.Value.BatchSize, cancellationToken);
        foreach (var row in batch)
        {
            try
            {
                var envelope = serializer.DeserializeEnvelope(row.EnvelopeJson);
                await bus.DispatchAsync(envelope, cancellationToken);
                await outbox.MarkDispatchedAsync(row.EventId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch outbox event {EventId}", row.EventId);
                await outbox.MarkFailedAsync(row.EventId, ex.ToString(), cancellationToken);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Outbox dispatcher disabled.");
            return;
        }

        logger.LogInformation("Outbox dispatcher started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatcher loop error.");
            }

            try
            {
                await Task.Delay(options.Value.PollIntervalMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
