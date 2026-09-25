using System.Net.Http.Json;
using System.Text.Json;
using ForgeDeck.Contracts.Pipelines;
using Microsoft.Extensions.Logging;

namespace ForgeDeck.Runner;

internal sealed class RunnerClient(HttpClient http, RunnerOptions options, ILogger<RunnerClient> logger)
{
    public async Task<RunnerCredentials> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(options.CredentialsPath))
        {
            var existing = JsonSerializer.Deserialize<RunnerCredentials>(
                await File.ReadAllTextAsync(options.CredentialsPath, cancellationToken), JsonDefaults.Options);
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.Token))
            {
                logger.LogInformation("Loaded durable runner credentials for {RunnerId}", existing.RunnerId);
                return existing;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.RunnerToken) && Guid.TryParse(Environment.GetEnvironmentVariable("RUNNER_ID"), out var runnerId))
        {
            var fromEnv = new RunnerCredentials(runnerId, options.RunnerToken);
            await PersistAsync(fromEnv, cancellationToken);
            return fromEnv;
        }

        if (string.IsNullOrWhiteSpace(options.RegistrationToken))
        {
            throw new InvalidOperationException("No credentials file and no --token / REGISTRATION_TOKEN provided.");
        }

        var capabilities = await CapabilityProbe.DetectAsync(cancellationToken);
        var request = new RunnerRegistrationRequest(
            options.RunnerName,
            options.RegistrationToken,
            OperatingSystem.IsWindows() ? "windows" : "linux",
            capabilities,
            Concurrency: 1,
            options.Version);

        var response = await http.PostAsJsonAsync("api/pipelines/runners/register", request, JsonDefaults.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RunnerRegistrationResponse>(JsonDefaults.Options, cancellationToken)
                   ?? throw new InvalidOperationException("Registration response was empty.");

        logger.LogInformation("Registered runner {RunnerId}. {Warning}", body.RunnerId, body.Warning);
        var credentials = new RunnerCredentials(body.RunnerId, body.RunnerToken);
        await PersistAsync(credentials, cancellationToken);
        return credentials;
    }

    public async Task<RunnerHeartbeatResponse> HeartbeatAsync(
        RunnerCredentials credentials,
        string status,
        IReadOnlyList<string> capabilities,
        int currentJobCount,
        CancellationToken cancellationToken,
        IReadOnlyList<Guid>? acknowledgedCancelJobIds = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/pipelines/runners/{credentials.RunnerId}/heartbeat");
        message.Headers.TryAddWithoutValidation("X-Runner-Token", credentials.Token);
        message.Content = JsonContent.Create(new RunnerHeartbeatRequest(
            status, capabilities, currentJobCount, options.Version,
            Environment.ProcessorCount, null,
            OperatingSystem.IsWindows() ? "windows" : "linux",
            acknowledgedCancelJobIds), options: JsonDefaults.Options);
        var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunnerHeartbeatResponse>(JsonDefaults.Options, cancellationToken)
               ?? new RunnerHeartbeatResponse([]);
    }

    public async Task<IReadOnlyList<JobAssignmentDto>> RequestWorkAsync(
        RunnerCredentials credentials, int maxJobs, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/pipelines/runners/{credentials.RunnerId}/work");
        message.Headers.TryAddWithoutValidation("X-Runner-Token", credentials.Token);
        message.Content = JsonContent.Create(new RunnerWorkRequest(maxJobs), options: JsonDefaults.Options);
        var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JobAssignmentDto>>(JsonDefaults.Options, cancellationToken) ?? [];
    }

    public Task AppendLogsAsync(
        RunnerCredentials credentials, Guid runId, Guid jobId, IReadOnlyList<LogChunkDto> chunks, CancellationToken cancellationToken) =>
        PostAsync(credentials, $"api/pipelines/runs/{runId}/jobs/{jobId}/logs", chunks, cancellationToken);

    public Task ReportStepAsync(
        RunnerCredentials credentials, Guid runId, Guid jobId, StepReportDto report, CancellationToken cancellationToken) =>
        PostAsync(credentials, $"api/pipelines/runs/{runId}/jobs/{jobId}/steps", report, cancellationToken);

    public Task CompleteJobAsync(
        RunnerCredentials credentials, Guid runId, JobCompleteDto report, CancellationToken cancellationToken) =>
        PostAsync(credentials, $"api/pipelines/runs/{runId}/jobs/complete", report, cancellationToken);

    public Task UploadTestsAsync(
        RunnerCredentials credentials, Guid runId, Guid jobId, TestResultUploadDto upload, CancellationToken cancellationToken) =>
        PostAsync(credentials, $"api/pipelines/runs/{runId}/jobs/{jobId}/tests", upload, cancellationToken);

    public async Task<bool> UploadArtifactAsync(
        RunnerCredentials credentials, Guid runId, Guid jobId, ArtifactUploadDto upload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/pipelines/runs/{runId}/jobs/{jobId}/artifacts");
        message.Headers.TryAddWithoutValidation("X-Runner-Token", credentials.Token);
        message.Content = JsonContent.Create(upload, options: JsonDefaults.Options);
        var response = await http.SendAsync(message, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task PostAsync<T>(RunnerCredentials credentials, string path, T body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Headers.TryAddWithoutValidation("X-Runner-Token", credentials.Token);
        message.Content = JsonContent.Create(body, options: JsonDefaults.Options);
        var response = await http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PersistAsync(RunnerCredentials credentials, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.CredentialsPath)!);
        await File.WriteAllTextAsync(
            options.CredentialsPath,
            JsonSerializer.Serialize(credentials, JsonDefaults.Options),
            cancellationToken);
        logger.LogInformation("Persisted runner credentials to {Path}", options.CredentialsPath);
    }
}
