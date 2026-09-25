using ForgeDeck.Build.Application;
using ForgeDeck.Build.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Build.Infrastructure;

/// <summary>
/// When Pipelines:ExecutionMode = Simulated, completes WaitingForRunner jobs without a shell
/// (same fail-on-"exit 1" behaviour as the former DeterministicForgeDeck.Runner).
/// </summary>
public sealed class SimulatedRunnerHostedService(
    IPipelineStore store,
    JobExecutionService execution,
    IOptions<PipelinesOptions> options,
    ILogger<SimulatedRunnerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(options.Value.ExecutionMode, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Pipelines ExecutionMode is {Mode}; SimulatedRunnerHostedService is idle.", options.Value.ExecutionMode);
            return;
        }

        logger.LogInformation("Simulated pipeline runner is active.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var run in store.ListRunsWithJobsWaitingForRunner())
                {
                    var job = run.Jobs.OrderBy(j => j.Ordinal).FirstOrDefault(j => j.Status == JobStatus.WaitingForRunner);
                    if (job is null)
                    {
                        continue;
                    }

                    execution.SimulateJob(run.Id, job.Id);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Simulated runner tick failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }
    }
}
