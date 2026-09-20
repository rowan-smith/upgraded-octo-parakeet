using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Modules;
using Platform.Core.Audit;
using Platform.Core.Context;

namespace Platform.Core.Api;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app, IReadOnlyList<IPlatformModule> modules)
    {
        app.MapGet("/api/platform/modules", (ICapabilityService capabilities) => Results.Ok(new
        {
            modules = modules.Select(module => new
            {
                module.Manifest.Id, module.Manifest.Name, module.Manifest.Version, module.Manifest.Edition,
                module.Manifest.Capabilities, module.Manifest.Navigation, module.Manifest.ResourceTabs, enabled = true
            }),
            capabilities = capabilities.Current
        }));
        app.MapGet("/api/core/context", (PlatformContextStore context) => Results.Ok(new { context.Organisation, context.Project, context.User }));
        app.MapGet("/api/core/audit", (AuditStore auditStore) => Results.Ok(auditStore.Snapshot()));
    }
}
