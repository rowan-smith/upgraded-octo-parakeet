using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Contracts.Extensions;
using Platform.Core.Context;
using Platform.Core.Extensions;
using Platform.Core.Identity;

namespace Platform.Core.Api;

public static class ExtensionEndpoints
{
    public static void MapExtensionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/platform/extensions", (ExtensionLifecycleService extensions, HttpRequest request) =>
        {
            ExtensionType? type = null;
            if (request.Query.TryGetValue("type", out var raw) &&
                Enum.TryParse<ExtensionType>(raw.ToString(), ignoreCase: true, out var parsed))
                type = parsed;
            return Results.Ok(new
            {
                extensions = extensions.List(type),
                composition = extensions.Composition()
            });
        });

        app.MapGet("/api/platform/extensions/modules", (ExtensionLifecycleService extensions) =>
            Results.Ok(extensions.List(ExtensionType.Module)));

        app.MapGet("/api/platform/extensions/connectors", (ExtensionLifecycleService extensions) =>
            Results.Ok(extensions.List(ExtensionType.Connector)));

        app.MapGet("/api/platform/extensions/{extensionId}", (string extensionId, ExtensionLifecycleService extensions) =>
        {
            try { return Results.Ok(extensions.Get(extensionId)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        app.MapPost("/api/platform/extensions/{extensionId}/install", (
            string extensionId,
            InstallExtensionRequest? request,
            ExtensionLifecycleService extensions,
            PermissionAuthorizer authorizer,
            HttpContext http,
            PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
                return PermissionAuthorizer.Forbidden();
            try
            {
                var status = extensions.Install(extensionId, context.User.Email, request?.Enable ?? true);
                return Results.Ok(status);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        app.MapPost("/api/platform/extensions/{extensionId}/enable", (
            string extensionId,
            ExtensionLifecycleService extensions,
            PermissionAuthorizer authorizer,
            HttpContext http,
            PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
                return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(extensions.Enable(extensionId, context.User.Email)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        app.MapPost("/api/platform/extensions/{extensionId}/disable", (
            string extensionId,
            ExtensionLifecycleService extensions,
            PermissionAuthorizer authorizer,
            HttpContext http,
            PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
                return PermissionAuthorizer.Forbidden();
            try { return Results.Ok(extensions.Disable(extensionId, context.User.Email)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        app.MapPost("/api/platform/extensions/{extensionId}/uninstall", (
            string extensionId,
            UninstallExtensionRequest? request,
            ExtensionLifecycleService extensions,
            PermissionAuthorizer authorizer,
            HttpContext http,
            PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
                return PermissionAuthorizer.Forbidden();
            try
            {
                return Results.Ok(extensions.Uninstall(extensionId, context.User.Email, request?.PreserveData ?? true));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
    }
}

public sealed record InstallExtensionRequest(bool Enable = true);
public sealed record UninstallExtensionRequest(bool PreserveData = true);
