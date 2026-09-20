using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;
using Platform.Contracts.Audit;
using Platform.Contracts.Pipelines;
using Platform.Core.Context;
using Platform.Core.Identity;
using Platform.Core.SourceControl;

namespace Modules.Pipelines.Api;

public static class PipelineEndpoints
{
    public static void MapPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pipelines");

        group.MapGet("/definitions", (PipelineService service) => Results.Ok(service.ListDefinitions()));
        group.MapGet("/definitions/{id:guid}", (Guid id, PipelineService service) =>
            service.FindDefinition(id) is { } definition ? Results.Ok(definition) : Results.NotFound());
        group.MapPost("/definitions", CreateDefinition);
        group.MapPut("/definitions/{id:guid}", UpdateDefinition);
        group.MapPost("/definitions/{id:guid}/enable", (Guid id, HttpContext ctx, PipelineService service, PlatformContextStore platform, PermissionAuthorizer auth, IAuditWriter audit) =>
            SetEnabled(id, true, ctx, service, platform, auth, audit));
        group.MapPost("/definitions/{id:guid}/disable", (Guid id, HttpContext ctx, PipelineService service, PlatformContextStore platform, PermissionAuthorizer auth, IAuditWriter audit) =>
            SetEnabled(id, false, ctx, service, platform, auth, audit));
        group.MapDelete("/definitions/{id:guid}", DeleteDefinition);

        group.MapGet("/runs", (PipelineService service) => Results.Ok(service.ListRuns()));
        group.MapGet("/runs/{id:guid}", (Guid id, PipelineService service) =>
            service.FindRun(id) is { } run ? Results.Ok(run) : Results.NotFound());
        group.MapPost("/runs", StartRun);
        group.MapPost("/runs/{id:guid}/cancel", CancelRun);
        group.MapPost("/runs/{id:guid}/retry", RetryRun);
        group.MapGet("/runs/{runId:guid}/jobs/{jobId:guid}/artifacts/{artifactId:guid}", DownloadArtifact);

        group.MapGet("/runners", ListRunners);
        group.MapPost("/runners/registration-tokens", CreateRegistrationToken);
        group.MapPost("/runners/register", RegisterRunner);
        group.MapPost("/runners/{id:guid}/heartbeat", Heartbeat);
        group.MapPost("/runners/{id:guid}/work", RequestWork);
        group.MapDelete("/runners/{id:guid}", RevokeRunner);

        group.MapPost("/runs/{runId:guid}/jobs/{jobId:guid}/logs", AppendLogs);
        group.MapPost("/runs/{runId:guid}/jobs/{jobId:guid}/steps", ReportStep);
        group.MapPost("/runs/{runId:guid}/jobs/complete", CompleteJob);
        group.MapPost("/runs/{runId:guid}/jobs/{jobId:guid}/tests", UploadTests);
        group.MapPost("/runs/{runId:guid}/jobs/{jobId:guid}/artifacts", UploadArtifact);
    }

    private static IResult CreateDefinition(
        CreatePipelineRequest request,
        HttpContext context,
        PipelineService service,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.manage")) return PermissionAuthorizer.Forbidden();
        try
        {
            var definition = service.AddDefinition(platform.Project.Id, request.Name, request.Triggers, request.Jobs, request.Environment, request.TimeoutSeconds);
            audit.Write("pipelines", "pipeline.created", definition.Id.ToString());
            return Results.Created($"/api/pipelines/definitions/{definition.Id}", definition);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult UpdateDefinition(
        Guid id,
        CreatePipelineRequest request,
        HttpContext context,
        PipelineService service,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.manage")) return PermissionAuthorizer.Forbidden();
        try
        {
            var definition = new PipelineDefinition
            {
                Id = id,
                Name = request.Name,
                Triggers = request.Triggers,
                Jobs = request.Jobs,
                Environment = request.Environment ?? new Dictionary<string, string>(),
                TimeoutSeconds = request.TimeoutSeconds
            };
            var updated = service.UpdateDefinition(platform.Project.Id, definition);
            audit.Write("pipelines", "pipeline.updated", id.ToString());
            return Results.Ok(updated);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult SetEnabled(
        Guid id,
        bool enabled,
        HttpContext context,
        PipelineService service,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.manage")) return PermissionAuthorizer.Forbidden();
        try
        {
            var definition = service.SetEnabled(platform.Project.Id, id, enabled);
            audit.Write("pipelines", enabled ? "pipeline.updated" : "pipeline.updated", id.ToString(), new { enabled });
            return Results.Ok(definition);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult DeleteDefinition(
        Guid id,
        HttpContext context,
        PipelineService service,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.manage")) return PermissionAuthorizer.Forbidden();
        try
        {
            service.DeleteDefinition(id);
            audit.Write("pipelines", "pipeline.deleted", id.ToString());
            return Results.NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult StartRun(
        RunPipelineRequest request,
        HttpContext context,
        PipelineService service,
        SourceRepositoryService sources,
        IConfiguration configuration,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.run")) return PermissionAuthorizer.Forbidden();
        try
        {
            var resolved = request.RepositoryUrl
                ?? sources.List().FirstOrDefault()?.Url
                ?? configuration["Source:DefaultRepository:Url"];
            var run = service.StartRun(
                request.DefinitionId,
                PipelineTrigger.Manual,
                request.Ref,
                request.CommitSha,
                repositoryUrl: resolved);
            audit.Write("pipelines", "pipeline.run.started", run.Id.ToString(), new { run.Status });
            return Results.Created($"/api/pipelines/runs/{run.Id}", run);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult CancelRun(
        Guid id,
        CancelPipelineRequest? request,
        HttpContext context,
        PipelineService service,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.cancel") && !authorizer.Has(context, "pipelines.run"))
            return PermissionAuthorizer.Forbidden();
        try
        {
            var run = service.CancelRun(id, request?.Reason);
            audit.Write("pipelines", "pipeline.run.cancelled", run.Id.ToString());
            return Results.Ok(run);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult RetryRun(
        Guid id,
        HttpContext context,
        PipelineService service,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.run")) return PermissionAuthorizer.Forbidden();
        try
        {
            var run = service.RetryRun(id);
            audit.Write("pipelines", "pipeline.run.retried", run.Id.ToString());
            return Results.Created($"/api/pipelines/runs/{run.Id}", run);
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult ListRunners(HttpContext context, RunnerService runners, PermissionAuthorizer authorizer)
    {
        if (!authorizer.Has(context, "pipelines.runner.read") && !authorizer.Has(context, "pipelines.read"))
            return PermissionAuthorizer.Forbidden();
        return Results.Ok(runners.ListRunners().Select(r => new
        {
            r.Id,
            r.Name,
            status = r.Status.ToString(),
            r.OperatingSystem,
            r.Capabilities,
            r.Concurrency,
            r.CurrentJobCount,
            r.Version,
            r.LastHeartbeatAt
        }));
    }

    private static IResult CreateRegistrationToken(
        CreateRegistrationTokenRequest request,
        HttpContext context,
        RunnerService runners,
        PlatformContextStore platform,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.runner.manage")) return PermissionAuthorizer.Forbidden();
        var (token, record) = runners.CreateRegistrationToken(platform.User.Email, TimeSpan.FromHours(Math.Clamp(request.LifetimeHours, 1, 168)));
        audit.Write("pipelines", "pipeline.runner.token.created", record.Id.ToString());
        return Results.Ok(new { token, record.Id, record.ExpiresAt });
    }

    private static IResult RevokeRunner(
        Guid id,
        HttpContext context,
        RunnerService runners,
        PermissionAuthorizer authorizer,
        IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.runner.manage")) return PermissionAuthorizer.Forbidden();
        try
        {
            runners.Revoke(id);
            audit.Write("pipelines", "runner.revoked", id.ToString());
            return Results.NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult DownloadArtifact(
        Guid runId,
        Guid jobId,
        Guid artifactId,
        PipelineService service,
        IOptions<PipelinesOptions> options)
    {
        var run = service.FindRun(runId);
        if (run is null) return Results.NotFound();
        var job = run.FindJob(jobId);
        var artifact = job?.Artifacts.FirstOrDefault(a => a.Id == artifactId);
        if (artifact is null) return Results.NotFound();
        var fullPath = Path.GetFullPath(Path.Combine(options.Value.ArtifactRoot, artifact.StoragePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(options.Value.ArtifactRoot);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return Results.NotFound();
        return Results.File(fullPath, artifact.ContentType, artifact.Name);
    }

    private static IResult RegisterRunner(RunnerRegistrationRequest request, RunnerService runners, IAuditWriter audit)
    {
        try
        {
            var response = runners.Register(request);
            audit.Write("pipelines", "runner.registered", response.RunnerId.ToString());
            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult Heartbeat(Guid id, RunnerHeartbeatRequest request, HttpRequest http, RunnerService runners)
    {
        try
        {
            var token = RequireRunnerToken(http);
            return Results.Ok(runners.Heartbeat(id, token, request));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static IResult RequestWork(Guid id, RunnerWorkRequest? request, HttpRequest http, RunnerService runners)
    {
        try
        {
            var token = RequireRunnerToken(http);
            return Results.Ok(runners.RequestWork(id, token, request ?? new RunnerWorkRequest()));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static IResult AppendLogs(
        Guid runId,
        Guid jobId,
        IReadOnlyList<LogChunkDto> chunks,
        HttpRequest http,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution)
    {
        try
        {
            AuthenticateJobRunner(http, pipelines, runners, runId, jobId);
            return Results.Ok(execution.AppendLogs(runId, jobId, chunks));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static IResult ReportStep(
        Guid runId,
        Guid jobId,
        StepReportDto report,
        HttpRequest http,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution)
    {
        try
        {
            AuthenticateJobRunner(http, pipelines, runners, runId, jobId);
            return Results.Ok(execution.ReportStep(runId, jobId, report));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            return exception is KeyNotFoundException
                ? Results.NotFound(new { error = exception.Message })
                : Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult CompleteJob(
        Guid runId,
        JobCompleteDto report,
        HttpRequest http,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution)
    {
        try
        {
            AuthenticateJobRunner(http, pipelines, runners, runId, report.JobId);
            return Results.Ok(execution.CompleteJob(runId, report));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            return exception is KeyNotFoundException
                ? Results.NotFound(new { error = exception.Message })
                : Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult UploadTests(
        Guid runId,
        Guid jobId,
        TestResultUploadDto upload,
        HttpRequest http,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution)
    {
        try
        {
            AuthenticateJobRunner(http, pipelines, runners, runId, jobId);
            if (!string.Equals(upload.Format, "trx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only trx format is supported." });
            var results = TrxParser.Parse(upload.Content);
            return Results.Ok(execution.AttachTestResults(runId, jobId, results));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Xml.XmlException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult UploadArtifact(
        Guid runId,
        Guid jobId,
        ArtifactUploadDto upload,
        HttpRequest http,
        PipelineService pipelines,
        RunnerService runners,
        JobExecutionService execution,
        IOptions<PipelinesOptions> options)
    {
        try
        {
            AuthenticateJobRunner(http, pipelines, runners, runId, jobId);
            if (upload.JobId != jobId)
                return Results.BadRequest(new { error = "Job id mismatch." });
            if (string.IsNullOrWhiteSpace(upload.ContentBase64))
                return Results.BadRequest(new { error = "ContentBase64 is required." });

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(upload.ContentBase64);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "ContentBase64 is not valid base64." });
            }

            var root = Path.GetFullPath(options.Value.ArtifactRoot);
            Directory.CreateDirectory(root);
            var safeName = string.Join("_", (upload.Name ?? "artifact").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "artifact";
            var relative = Path.Combine(runId.ToString("N"), jobId.ToString("N"), $"{Guid.NewGuid():N}-{safeName}");
            var fullPath = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);

            var artifact = new RunArtifact
            {
                JobId = jobId,
                Name = string.IsNullOrWhiteSpace(upload.Name) ? safeName : upload.Name,
                ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType,
                Size = upload.Size > 0 ? upload.Size : bytes.LongLength,
                StoragePath = relative.Replace('\\', '/')
            };
            return Results.Ok(execution.AttachArtifact(runId, jobId, artifact));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException or IOException)
        {
            return exception is KeyNotFoundException
                ? Results.NotFound(new { error = exception.Message })
                : Results.BadRequest(new { error = exception.Message });
        }
    }

    private static string RequireRunnerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Runner-Token", out var values) || string.IsNullOrWhiteSpace(values))
            throw new UnauthorizedAccessException("Missing runner token.");
        return values.ToString();
    }

    private static void AuthenticateJobRunner(
        HttpRequest request,
        PipelineService pipelines,
        RunnerService runners,
        Guid runId,
        Guid jobId)
    {
        var token = RequireRunnerToken(request);
        var run = pipelines.FindRun(runId) ?? throw new KeyNotFoundException("Run was not found.");
        var job = run.FindJob(jobId) ?? throw new KeyNotFoundException("Job was not found.");
        if (job.RunnerId is null)
            throw new UnauthorizedAccessException("Job has no assigned runner.");
        runners.Authenticate(job.RunnerId.Value, token);
    }
}
