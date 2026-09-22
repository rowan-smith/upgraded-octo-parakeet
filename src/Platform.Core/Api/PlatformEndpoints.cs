using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Modules;
using Platform.Core.Audit;
using Platform.Core.Context;
using Platform.Core.Extensions;

namespace Platform.Core.Api;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app, IReadOnlyList<IPlatformModule> modules)
    {
        app.MapGet("/api/platform/modules", (ExtensionLifecycleService extensions, ICapabilityService capabilities, PlatformContextStore context) =>
            Results.Ok(new
            {
                modules = extensions.ActiveModulesForSpa(),
                capabilities = capabilities.ForOrganisation(context.Organisation.Id)
            }));
        app.MapGet("/api/core/context", (PlatformContextStore context) => Results.Ok(new { context.Organisation, context.Project, context.User }));
        app.MapGet("/api/core/audit", (AuditStore auditStore) => Results.Ok(auditStore.Snapshot()));
    }
}
