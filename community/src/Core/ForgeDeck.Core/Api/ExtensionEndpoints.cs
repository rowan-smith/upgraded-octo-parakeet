using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Core.Context;
using ForgeDeck.Core.Extensions;
using ForgeDeck.Core.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Api;

public static class ExtensionEndpoints
{
    public static void MapExtensionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/platform/extensions", (ExtensionLifecycleService extensions, HttpRequest request) =>
        {
            ExtensionType? type = null;
            if (request.Query.TryGetValue("type", out var raw) &&
                Enum.TryParse<ExtensionType>(raw.ToString(), ignoreCase: true, out var parsed))
            {
                type = parsed;
            }

            return Results.Ok(new
            {
                extensions = extensions.List(type),
                composition = extensions.Composition()
            });
        });

        app.MapGet("/api/platform/extensions/health", (ExtensionLifecycleService extensions) =>
            Results.Ok(extensions.List().Select(x => new
            {
                id = x.ExtensionId,
                state = x.State.ToString(),
                health = x.Health.ToString(),
                version = x.Version,
                lastError = x.LastError,
                installedFrom = x.InstalledFrom.ToString(),
                packageDigest = x.PackageDigest
            })));

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
            {
                return PermissionAuthorizer.Forbidden();
            }

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
            {
                return PermissionAuthorizer.Forbidden();
            }

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
            {
                return PermissionAuthorizer.Forbidden();
            }

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
            {
                return PermissionAuthorizer.Forbidden();
            }

            try
            {
                return Results.Ok(extensions.Uninstall(extensionId, context.User.Email, request?.PreserveData ?? true));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });

        app.MapPost("/api/platform/extensions/upload", async (
            HttpRequest request,
            ExtensionLifecycleService extensions,
            PermissionAuthorizer authorizer,
            HttpContext http,
            PlatformContextStore context) =>
        {
            if (!authorizer.Has(http, OrganisationPermissions.ModulesManage))
            {
                return PermissionAuthorizer.Forbidden();
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart form upload." });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "A package file is required (field name 'file')." });
            }

            var name = file.FileName ?? "";
            if (!name.EndsWith(".fdext", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Package must be a .fdext or .zip archive." });
            }

            var enable = !form.TryGetValue("enable", out var enableRaw)
                         || !bool.TryParse(enableRaw.ToString(), out var parsed)
                         || parsed;
            var extensionId = form.TryGetValue("extensionId", out var idRaw) ? idRaw.ToString() : null;
            var version = form.TryGetValue("version", out var verRaw) ? verRaw.ToString() : null;
            var checksum = form.TryGetValue("sha256", out var hashRaw) ? hashRaw.ToString() : null;

            try
            {
                await using var stream = file.OpenReadStream();
                var status = extensions.InstallFromPackageFile(
                    stream,
                    context.User.Email,
                    string.IsNullOrWhiteSpace(extensionId) ? null : extensionId,
                    string.IsNullOrWhiteSpace(version) ? null : version,
                    string.IsNullOrWhiteSpace(checksum) ? null : checksum,
                    enable);
                return Results.Ok(status);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}

public sealed record InstallExtensionRequest(bool Enable = true);
public sealed record UninstallExtensionRequest(bool PreserveData = true);
