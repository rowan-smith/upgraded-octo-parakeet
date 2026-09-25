using System.Collections.Concurrent;
using System.Net.Sockets;
using ForgeDeck.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
var options = RunnerOptions.Parse(args, builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<RunnerClient>(client =>
{
    client.BaseAddress = new Uri(options.PlatformUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddSingleton<WorkspaceManager>();
builder.Services.AddSingleton<ProcessExecutor>();
builder.Services.AddSingleton<JobWorker>();
builder.Services.AddHostedService<RunnerHostedService>();

await builder.Build().RunAsync();

internal sealed class RunnerHostedService(
    RunnerOptions options,
    RunnerClient client,
    JobWorker worker,
    ILogger<RunnerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeJobs = new();
    private readonly ConcurrentDictionary<Guid, byte> _pendingCancelAcks = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Connecting to platform at {Url}…", options.PlatformUrl);
        RunnerCredentials credentials;
        while (true)
        {
            try
            {
                credentials = await client.EnsureRegisteredAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (IsTransientConnectivity(exception))
            {
                logger.LogWarning("Platform unreachable at {Url} ({Message}). Retrying in 5s…", options.PlatformUrl, exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Runner {RunnerId} ({Name}) connected to {Url}", credentials.RunnerId, options.RunnerName, options.PlatformUrl);

        var capabilities = await CapabilityProbe.DetectAsync(stoppingToken);
        var lastHeartbeat = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastHeartbeat >= HeartbeatInterval)
                {
                    var toAck = _pendingCancelAcks.Keys.ToArray();
                    var heartbeat = await client.HeartbeatAsync(
                        credentials,
                        _activeJobs.IsEmpty ? "Online" : "Busy",
                        capabilities,
                        _activeJobs.Count,
                        stoppingToken,
                        toAck.Length > 0 ? toAck : null);
                    foreach (var id in toAck)
                    {
                        _pendingCancelAcks.TryRemove(id, out _);
                    }

                    ApplyCancellations(heartbeat.CancelJobIds);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                if (!_activeJobs.IsEmpty)
                {
                    await Task.Delay(PollDelay, stoppingToken);
                    continue;
                }

                var jobs = await client.RequestWorkAsync(credentials, 1, stoppingToken);
                if (jobs.Count == 0)
                {
                    await Task.Delay(PollDelay, stoppingToken);
                    continue;
                }

                foreach (var job in jobs)
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _activeJobs[job.JobId] = linked;
                    try
                    {
                        await worker.ExecuteAsync(credentials, job, linked.Token);
                    }
                    finally
                    {
                        _activeJobs.TryRemove(job.JobId, out _);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Runner loop failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void ApplyCancellations(IReadOnlyList<Guid> cancelJobIds)
    {
        foreach (var jobId in cancelJobIds)
        {
            if (_activeJobs.TryGetValue(jobId, out var cts) && !cts.IsCancellationRequested)
            {
                logger.LogInformation("Cancelling job {JobId} as requested by control plane.", jobId);
                cts.Cancel();
            }
            _pendingCancelAcks[jobId] = 0;
        }
    }

    private static bool IsTransientConnectivity(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return true;
        }

        if (exception is HttpRequestException http)
        {
            // Auth / client errors are permanent until config changes — don't spin as "unreachable".
            if (http.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError)
            {
                return false;
            }

            return true;
        }
        return exception.InnerException is SocketException or HttpRequestException;
    }
}
