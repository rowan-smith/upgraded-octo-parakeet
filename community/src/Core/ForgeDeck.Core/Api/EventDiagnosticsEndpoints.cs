using ForgeDeck.Contracts.Events;
using ForgeDeck.Core.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Core.Api;

public static class EventDiagnosticsEndpoints
{
    public static RouteGroupBuilder MapEventDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/events");

        group.MapGet("/", async (IEventDiagnostics diagnostics, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var recent = await diagnostics.ListRecentOutboxAsync(50);
            return Results.Ok(new
            {
                recent,
                subscriptions = ProjectSubscriptions(await diagnostics.ListSubscriptionsAsync()),
                backlog = await diagnostics.ListOutboxBacklogAsync(),
                failures = await diagnostics.ListFailedDeliveriesAsync(50)
            });
        });

        group.MapGet("/outbox", async (IEventDiagnostics diagnostics, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(await diagnostics.ListRecentOutboxAsync(100));
        });

        group.MapGet("/failures", async (IEventDiagnostics diagnostics, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(await diagnostics.ListFailedDeliveriesAsync(100));
        });

        group.MapGet("/subscriptions", async (IEventDiagnostics diagnostics, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            return Results.Ok(ProjectSubscriptions(await diagnostics.ListSubscriptionsAsync()));
        });

        group.MapGet("/trace/{correlationId:guid}", async (
            Guid correlationId,
            IEventDiagnostics diagnostics,
            PermissionAuthorizer authorizer,
            HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var trace = await diagnostics.GetCorrelationTraceAsync(correlationId);
            return trace is null ? Results.NotFound() : Results.Ok(trace);
        });

        group.MapGet("/contracts", (IEventRegistry registry, PermissionAuthorizer authorizer, HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage)
                && !authorizer.Has(http, OrganisationPermissions.AuditRead))
            {
                return PermissionAuthorizer.Forbidden();
            }

            var contracts = registry.List().Select(c => new
            {
                c.Type,
                c.Version,
                ClrType = c.ClrType.FullName
            });
            return Results.Ok(contracts);
        });

        group.MapPost("/deliveries/{eventId:guid}/{consumerId}/retry", async (
            Guid eventId,
            string consumerId,
            IEventDiagnostics diagnostics,
            PermissionAuthorizer authorizer,
            HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            await diagnostics.RetryDeliveryAsync(eventId, Uri.UnescapeDataString(consumerId));
            return Results.Accepted();
        });

        group.MapPost("/deliveries/{eventId:guid}/{consumerId}/acknowledge", async (
            Guid eventId,
            string consumerId,
            IEventDiagnostics diagnostics,
            PermissionAuthorizer authorizer,
            HttpContext http) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            await diagnostics.AcknowledgeDeliveryAsync(eventId, Uri.UnescapeDataString(consumerId));
            return Results.Ok();
        });

        return group;
    }

    private static object ProjectSubscriptions(IReadOnlyList<EventSubscriptionInfo> subscriptions) =>
        subscriptions.Select(s => new
        {
            s.ConsumerId,
            s.EventType,
            s.EventVersion,
            HandlerType = s.HandlerType.FullName,
            s.Active
        }).ToArray();
}
