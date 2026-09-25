using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForgeDeck.Review.Infrastructure;

/// <summary>Periodically refreshes open Changes from their source provider (Phase 1 §42).</summary>
public sealed class ChangeRefreshHostedService(
    IServiceScopeFactory scopes,
    ILogger<ChangeRefreshHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var review = scope.ServiceProvider.GetRequiredService<ReviewService>();
                var open = review.List(status: "Open")
                    .Concat(review.List(status: "Approved"))
                    .Concat(review.List(status: "Changes Requested"))
                    .Concat(review.List(status: "Draft"))
                    .GroupBy(c => c.Id)
                    .Select(g => g.First())
                    .ToArray();

                foreach (var change in open)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try
                    {
                        await review.GetAndRefreshAsync(change.Id, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "Failed to refresh change {ChangeId}", change.Id);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Change refresh sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
