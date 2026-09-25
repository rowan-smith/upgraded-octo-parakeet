using ForgeDeck.Contracts.Audit;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.SourceControl;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Identity;
using ForgeDeck.Review.Application;
using ForgeDeck.Review.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Review.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/review");
        group.MapGet("/changes", (string? status, string? author, string? reviewer, ReviewService service) => Results.Ok(service.List(status, author, reviewer)));
        group.MapGet("/changes/{id:guid}", GetChange);
        group.MapGet("/changes/{id:guid}/diff", GetDiff);
        group.MapGet("/changes/{id:guid}/diff/file", GetFileDiff);
        group.MapGet("/changes/{id:guid}/checks", async (Guid id, CheckQueryService checks, CancellationToken token) => Results.Ok(new { changeId = id, checks = await checks.GetAsync(id, token) }));
        group.MapGet("/external-changes", Discover);
        group.MapPost("/changes/import", ImportChange);
        group.MapPost("/changes", CreateChange);
        group.MapPost("/changes/{id:guid}/refresh", Refresh);
        group.MapPost("/changes/{id:guid}/comments", AddComment);
        group.MapPut("/changes/{id:guid}/comments/{commentId:guid}", EditComment);
        group.MapDelete("/changes/{id:guid}/comments/{commentId:guid}", DeleteComment);
        group.MapPost("/changes/{id:guid}/discussions/{discussionId:guid}/resolve", (Guid id, Guid discussionId, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.comment", () => service.SetDiscussionResolution(id, discussionId, Actor(context), true), audit, "review.discussion.resolved", id));
        group.MapPost("/changes/{id:guid}/discussions/{discussionId:guid}/reopen", (Guid id, Guid discussionId, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
            Mutation(context, auth, "review.comment", () => service.SetDiscussionResolution(id, discussionId, Actor(context), false), audit, "review.discussion.reopened", id));
        group.MapPost("/changes/{id:guid}/reviewers", AddReviewer);
        group.MapDelete("/changes/{id:guid}/reviewers/{name}", RemoveReviewer);
        group.MapPost("/changes/{id:guid}/reviews", SubmitReview);
        group.MapPost("/changes/{id:guid}/approve", Approve);
        group.MapPost("/changes/{id:guid}/request-changes", RequestChanges);
        group.MapPost("/changes/{id:guid}/merge", Merge);
        group.MapPost("/changes/{id:guid}/close", Close);
        group.MapGet("/policy", (ReviewService service) => Results.Ok(service.Policy));
        group.MapPut("/policy", ConfigurePolicy);
    }

    private static IResult ConfigurePolicy(
        PolicyRequest request,
        HttpContext context,
        PermissionAuthorizer auth,
        CapabilityAuthorizer capabilities,
        ApprovalPolicyResolver policies,
        IAuditWriter audit)
    {
        if (!auth.Has(context, "review.manage"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            policies.Configure(request.MinimumApprovals, capabilities);
            audit.Write("review", "review.policy.updated", "policy", new { request.MinimumApprovals });
            return Results.Ok(policies.Snapshot());
        }
        catch (LicenceRequiredException exception)
        {
            return CapabilityAuthorizer.Forbidden(exception.Capability);
        }
    }

    private static async Task<IResult> GetChange(Guid id, bool? refresh, ReviewService service, CancellationToken token)
    {
        var response = await service.GetResponseAsync(id, refresh != false, token);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static IResult GetDiff(Guid id, string? path, ReviewService service)
    {
        var change = service.Find(id); if (change is null)
        {
            return Results.NotFound();
        }

        var files = string.IsNullOrWhiteSpace(path)
            ? (IReadOnlyList<SourceDiffFile>)change.Files
            : change.Files.Where(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Results.Ok(new SourceDiff(change.BaseCommit, change.HeadCommit, files));
    }

    private static async Task<IResult> GetFileDiff(Guid id, string path, ReviewService service, CancellationToken token)
    {
        try
        {
            var diff = await service.GetFileDiffAsync(id, path, token);
            return diff is null ? Results.NotFound() : Results.Ok(diff);
        }
        catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    }

    private static async Task<IResult> Discover(Guid repositoryConnectionId, ReviewService service, CancellationToken token) =>
        Results.Ok(await service.DiscoverAsync(repositoryConnectionId, token));

    private static async Task<IResult> ImportChange(ImportRequest request, ReviewService service, IAuditWriter audit, CancellationToken token)
    {
        var change = request.RepositoryConnectionId is { } id
            ? await service.ImportAsync(id, request.ExternalId, token)
            : await service.ImportAsync(request.ProviderId, request.RepositoryUrl ?? throw new ArgumentException("Repository connection or URL is required."), request.ExternalId, token);
        audit.Write("review", "review.change.imported", change.Id.ToString(), new { change.ExternalId, change.ExternalUrl });
        return Results.Created($"/api/review/changes/{change.Id}", await service.ToResponseAsync(change, token));
    }

    private static async Task<IResult> CreateChange(CreateChangeRequest request, ReviewService service, IAuditWriter audit, CancellationToken token)
    {
        var change = await service.CreateAsync(request.RepositoryConnectionId, new(request.Title, request.Description, request.SourceBranch, request.TargetBranch), token);
        audit.Write("review", "review.change.created", change.Id.ToString(), new { change.ExternalId, change.ExternalUrl });
        return Results.Created($"/api/review/changes/{change.Id}", await service.ToResponseAsync(change, token));
    }

    private static async Task<IResult> Refresh(Guid id, ReviewService service, CancellationToken token)
    {
        var change = await service.GetAndRefreshAsync(id, token);
        return change is null ? Results.NotFound() : Results.Ok(await service.ToResponseAsync(change, token));
    }

    private static IResult AddComment(Guid id, CommentRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.comment", () => service.AddComment(id, Actor(context), request.Body, request.File, request.Side, request.Line, request.CommitSha, request.ParentId),
            audit, "review.comment.created", id, new { request.File, request.Line, request.ParentId });

    private static IResult EditComment(Guid id, Guid commentId, CommentRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.comment", () => service.EditComment(id, commentId, Actor(context), request.Body), audit, "review.comment.edited", id);

    private static IResult DeleteComment(Guid id, Guid commentId, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.comment", () => service.DeleteComment(id, commentId, Actor(context)), audit, "review.comment.deleted", id);

    private static IResult AddReviewer(Guid id, ReviewerRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.request", () => service.AddReviewer(id, Actor(context), request.Name), audit, "review.reviewer.requested", id, new { reviewer = request.Name });

    private static IResult RemoveReviewer(Guid id, string name, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit) =>
        Mutation(context, auth, "review.request", () => service.RemoveReviewer(id, Actor(context), name), audit, "review.reviewer.removed", id, new { reviewer = name });

    private static async Task<IResult> SubmitReview(Guid id, ReviewRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token)
    {
        try
        {
            return await MutationAsync(context, auth, request.State == "Comment" ? "review.comment" : "review.approve",
                () => service.SubmitReview(id, Actor(context), request.State, request.Body),
                service, audit, request.State == "Approved" ? "review.approved" : request.State == "ChangesRequested" ? "review.changes_requested" : "review.review.submitted", id, token);
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message, title = "Policy Failure" }); }
    }

    private static async Task<IResult> Approve(Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token) =>
        await MutationAsync(context, auth, "review.approve", () => service.Approve(id, Actor(context)), service, audit, "review.change.approved", id, token);

    private static async Task<IResult> RequestChanges(Guid id, CommentRequest request, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token) =>
        await MutationAsync(context, auth, "review.approve", () => service.RequestChanges(id, Actor(context), request.Body), service, audit, "review.change.changes_requested", id, token);

    private static async Task<IResult> Merge(Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token)
    {
        if (!auth.Has(context, "review.merge"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var change = await service.MergeAsync(id, Actor(context), token); if (change is null)
            {
                return Results.NotFound();
            }

            audit.Write("review", "review.change.merged", id.ToString(), new { change.MergeCommitSha });
            return Results.Ok(await service.ToResponseAsync(change, token));
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message, title = "Policy Failure" }); }
        catch (SourceProviderException exception) when (exception.Kind == SourceProviderErrorKind.Conflict)
        {
            return Results.Conflict(new { error = "The source and target branches contain conflicts. Resolve the conflicts locally and push the updated branch.", title = "Merge Conflict" });
        }
    }

    private static async Task<IResult> Close(Guid id, HttpContext context, ReviewService service, PermissionAuthorizer auth, IAuditWriter audit, CancellationToken token)
    {
        if (!auth.Has(context, "review.manage"))
        {
            return PermissionAuthorizer.Forbidden();
        }

        var change = await service.CloseAsync(id, Actor(context), token); if (change is null)
        {
            return Results.NotFound();
        }

        audit.Write("review", "review.change.closed", id.ToString());
        return Results.Ok(await service.ToResponseAsync(change, token));
    }

    private static IResult Mutation(HttpContext context, PermissionAuthorizer auth, string permission, Func<object?> action, IAuditWriter audit, string auditAction, Guid id, object? metadata = null)
    {
        if (!auth.Has(context, permission))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var result = action(); if (result is null)
            {
                return Results.NotFound();
            }

            audit.Write("review", auditAction, id.ToString(), metadata); return Results.Ok(result);
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message, title = "Validation Error" }); }
    }

    private static async Task<IResult> MutationAsync(
        HttpContext context,
        PermissionAuthorizer auth,
        string permission,
        Func<Change?> action,
        ReviewService service,
        IAuditWriter audit,
        string auditAction,
        Guid id,
        CancellationToken token,
        object? metadata = null)
    {
        if (!auth.Has(context, permission))
        {
            return PermissionAuthorizer.Forbidden();
        }

        try
        {
            var change = action();
            if (change is null)
            {
                return Results.NotFound();
            }

            audit.Write("review", auditAction, id.ToString(), metadata);
            return Results.Ok(await service.ToResponseAsync(change, token));
        }
        catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message, title = "Validation Error" }); }
    }

    private static string Actor(HttpContext context) => context.Items["user"] is PlatformUser user ? user.Name : "Unknown user";
}
