using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Pipelines.Application;
using Modules.Pipelines.Domain;
using Platform.Contracts.Audit;
using Platform.Core.Identity;

namespace Modules.Pipelines.Api;

public static class PipelineEndpoints
{
    public static void MapPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pipelines");
        group.MapGet("/definitions", (PipelineService service) => Results.Ok(service.ListDefinitions()));
        group.MapGet("/runs", (PipelineService service) => Results.Ok(service.ListRuns()));
        group.MapGet("/runs/{id:guid}", (Guid id, PipelineService service) => service.FindRun(id) is { } run ? Results.Ok(run) : Results.NotFound());
        group.MapPost("/definitions", CreateDefinition);
        group.MapPost("/runs", StartRun);
        group.MapGet("/runners", (IPipelineRunner runner) => Results.Ok(new[] { new { id = "local-mvp", runner.Name, status = "Online", jobs = 0 } }));
    }
    private static IResult CreateDefinition(CreatePipelineRequest request, HttpContext context, PipelineService service, PermissionAuthorizer authorizer, IAuditWriter audit)
    {
        if (!authorizer.Has(context, "pipelines.manage")) return Results.Forbid();
        try { var definition = service.AddDefinition(request.Name, request.Triggers, request.Steps, request.Environment); audit.Write("pipelines", "pipeline.definition.created", definition.Id.ToString()); return Results.Created($"/api/pipelines/definitions/{definition.Id}", definition); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    }
    private static async Task<IResult> StartRun(RunPipelineRequest request, HttpContext context, PipelineService service, PermissionAuthorizer authorizer, IAuditWriter audit, CancellationToken token)
    {
        if (!authorizer.Has(context, "pipelines.run")) return Results.Forbid();
        try { var run = await service.RunAsync(request.DefinitionId, PipelineTrigger.Manual, request.Ref, token: token); audit.Write("pipelines", "pipeline.run.completed", run.Id.ToString(), new { run.Status }); return Results.Created($"/api/pipelines/runs/{run.Id}", run); }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    }
}
