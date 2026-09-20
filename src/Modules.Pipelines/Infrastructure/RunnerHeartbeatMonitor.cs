using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;

namespace Modules.Pipelines.Infrastructure;

/// <summary>Marks runners offline when heartbeats stop and fails assigned jobs as Lost after grace.</summary>
public sealed class RunnerHeartbeatMonitor(
    IPipelineStore store,
    IOptions<PipelinesOptions> options,
    ILogger<RunnerHeartbeatMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Runner heartbeat sweep failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var offlineAfter = TimeSpan.FromSeconds(Math.Max(5, options.Value.RunnerHeartbeatTimeoutSeconds));
        var lostAfter = TimeSpan.FromSeconds(Math.Max(5, options.Value.RunnerDisconnectGraceSeconds));

        foreach (var runner in store.ListRunners())
        {
            if (runner.Status == RunnerStatus.Offline) continue;
            if (runner.LastHeartbeatAt is null) continue;
            if (now - runner.LastHeartbeatAt.Value < offlineAfter) continue;

            runner.MarkOffline();
            store.SaveRunner(runner);
            logger.LogWarning("Runner {RunnerId} ({Name}) marked offline after missed heartbeats.", runner.Id, runner.Name);
        }

        foreach (var run in store.ListRuns(200).Where(r => r.Status == PipelineRunStatus.Running))
        {
            var dirty = false;
            foreach (var job in run.Jobs.Where(j => j.Status is JobStatus.Assigned or JobStatus.Running))
            {
                if (job.RunnerId is not Guid runnerId) continue;
                var runner = store.FindRunner(runnerId);
                if (runner is null || runner.Status != RunnerStatus.Offline) continue;
                if (runner.LastHeartbeatAt is null) continue;
                if (now - runner.LastHeartbeatAt.Value < lostAfter) continue;

                job.MarkLost($"Runner '{runner.Name}' disconnected and did not reconnect.");
                dirty = true;
            }

            if (!dirty) continue;
            foreach (var completed in run.Jobs.Where(j => j.Status is JobStatus.Lost).ToArray())
                run.AdvanceAfterJobCompletion(completed.Id);
            store.SaveRun(run);
        }
    }
}
