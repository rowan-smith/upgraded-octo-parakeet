using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Review.Application;
using Platform.Contracts.Audit;
using Platform.Contracts.SourceControl;
using Platform.Core.Context;
using Platform.Core.Identity;

namespace Modules.Review.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/review");
        group.MapGet("/changes", (string? status, string? author, string? reviewer, ReviewService service) => Results.Ok(service.List(status, author, reviewer)));
        group.MapGet("/changes/{id:guid}", GetChange);
        group.MapGet("/changes/{id:guid}/diff", (Guid id, ReviewService service) => service.Find(id) is { } change ? Results.Ok(new SourceDiff(change.BaseCommit, change.HeadCommit, change.Files)) : Results.NotFound());
        group.MapGet("/changes/{id:guid}/checks", async (Guid id, CheckQueryService checks, CancellationToken token) => Results.Ok(new { changeId = id, checks = await checks.GetAsync(id, token) }));
        group.MapPost("/changes/import", ImportChange);
        group.MapPost("/changes", CreateChange);
        group.MapPost("/changes/{id:guid}/refresh", Refresh);
        group.MapPost("/changes/{id:guid}/comments", AddComment);
        group.MapPost("/changes/{id:guid}/discussions/{discussionId:guid}/resolve", (Guid id, Guid discussionId, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.comment", () => service.SetDiscussionResolution(id, discussionId, Actor(context), true), audit, "review.discussion.resolved", id));
        group.MapPost("/changes/{id:guid}/discussions/{discussionId:guid}/reopen", (Guid id, Guid discussionId, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.comment", () => service.SetDiscussionResolution(id, discussionId, Actor(context), false), audit, "review.discussion.reopened", id));
        group.MapPost("/changes/{id:guid}/reviewers", AddReviewer);
        group.MapDelete("/changes/{id:guid}/reviewers/{name}", RemoveReviewer);
        group.MapPost("/changes/{id:guid}/reviews", SubmitReview);
        group.MapPost("/changes/{id:guid}/approve", (Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.approve", () => service.Approve(id, Actor(context)), audit, "review.change.approved", id));
        group.MapPost("/changes/{id:guid}/request-changes", (Guid id, CommentRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.approve", () => service.RequestChanges(id, Actor(context), request.Body), audit, "review.change.changes_requested", id));
        group.MapPost("/changes/{id:guid}/merge", Merge);
        group.MapPost("/changes/{id:guid}/close", Close);
    }

    private static async Task<IResult> GetChange(Guid id, bool? refresh, ReviewService service, CancellationToken token)
    {
        var change = refresh == false ? service.Find(id) : await service.GetAndRefreshAsync(id, token);
        return change is null ? Results.NotFound() : Results.Ok(change);
    }

    private static async Task<IResult> ImportChange(ImportRequest request, ReviewService service, IAuditWriter audit, CancellationToken token)
    {
        var change = request.RepositoryConnectionId is { } id
            ? await service.ImportAsync(id, request.ExternalId, token)
            : await service.ImportAsync(request.ProviderId, request.RepositoryUrl ?? throw new ArgumentException("Repository connection or URL is required."), request.ExternalId, token);
        audit.Write("review", "review.change.created", change.Id.ToString(), new { change.ExternalId, change.ExternalUrl });
        return Results.Created($"/api/review/changes/{change.Id}", change);
    }

    private static async Task<IResult> CreateChange(CreateChangeRequest request, ReviewService service, IAuditWriter audit, CancellationToken token)
    {
        var change = await service.CreateAsync(request.RepositoryConnectionId, new(request.Title, request.Description, request.SourceBranch, request.TargetBranch), token);
        audit.Write("review", "review.change.created", change.Id.ToString(), new { change.ExternalId, change.ExternalUrl });
        return Results.Created($"/api/review/changes/{change.Id}", change);
    }

    private static async Task<IResult> Refresh(Guid id, ReviewService service, CancellationToken token) =>
        await service.GetAndRefreshAsync(id, token) is { } change ? Results.Ok(change) : Results.NotFound();

    private static IResult AddComment(Guid id, CommentRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.comment", () => service.AddComment(id, Actor(context), request.Body, request.File, request.Side, request.Line, request.CommitSha, request.ParentId),
            audit, "review.comment.created", id, new { request.File, request.Line, request.ParentId });

    private static IResult AddReviewer(Guid id, ReviewerRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.request", () => service.AddReviewer(id, Actor(context), request.Name), audit, "review.reviewer.requested", id, new { reviewer = request.Name });

    private static IResult RemoveReviewer(Guid id, string name, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.request", () => service.RemoveReviewer(id, Actor(context), name), audit, "review.reviewer.removed", id, new { reviewer = name });

    private static IResult SubmitReview(Guid id, ReviewRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, request.State == "Comment" ? "review.comment" : "review.approve", () => service.SubmitReview(id, Actor(context), request.State, request.Body),
            audit, request.State == "Approved" ? "review.change.approved" : request.State == "ChangesRequested" ? "review.change.changes_requested" : "review.review.submitted", id);

    private static async Task<IResult> Merge(Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token)
    {
        if (!auth.Has(context, "review.merge")) return Results.Forbid();
        try
        {
            var change = await service.MergeAsync(id, Actor(context), token); if (change is null) return Results.NotFound();
            audit.Write("review", "review.change.merged", id.ToString(), new { change.MergeCommitSha }); return Results.Ok(change);
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    }

    private static async Task<IResult> Close(Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token)
    {
        if (!auth.Has(context, "review.manage")) return Results.Forbid();
        var change = await service.CloseAsync(id, Actor(context), token); if (change is null) return Results.NotFound();
        audit.Write("review", "review.change.closed", id.ToString()); return Results.Ok(change);
    }

    private static IResult Mutation(HttpContext context, PermissionAuthorizer auth, string permission, Func<object?> action, IAuditWriter audit, string auditAction, Guid id, object? metadata = null)
    {
        if (!auth.Has(context, permission)) return Results.Forbid();
        var result = action(); if (result is null) return Results.NotFound(); audit.Write("review", auditAction, id.ToString(), metadata); return Results.Ok(result);
    }

    private static string Actor(HttpContext context) => context.Items["user"] is PlatformUser user ? user.Name : "Unknown user";
}
