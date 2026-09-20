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
        app.MapGet("/api/platform/modules", (ICapabilityService capabilities, PlatformContextStore context) =>
        {
            var organisationId = context.Organisation.Id;
            var granted = capabilities.ForOrganisation(organisationId);
            return Results.Ok(new
            {
                modules = modules.Select(module =>
                {
                    var moduleCapabilities = granted
                        .Where(capability => BelongsToModule(capability, module.Manifest))
                        .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return new
                    {
                        module.Manifest.Id,
                        module.Manifest.Name,
                        module.Manifest.Version,
                        edition = capabilities.EditionFor(organisationId, module.Manifest.Id, module.Manifest.Edition),
                        capabilities = moduleCapabilities,
                        module.Manifest.Navigation,
                        module.Manifest.ResourceTabs,
                        enabled = true
                    };
                }),
                capabilities = granted
            });
        });
        app.MapGet("/api/core/context", (PlatformContextStore context) => Results.Ok(new { context.Organisation, context.Project, context.User }));
        app.MapGet("/api/core/audit", (AuditStore auditStore) => Results.Ok(auditStore.Snapshot()));
    }

    private static bool BelongsToModule(string capability, ModuleManifest manifest)
    {
        var dot = capability.IndexOf('.');
        if (dot <= 0) return false;
        var prefix = capability[..dot];
        var commercial = KnownCapabilities.IsCommercial(capability);
        var isCommercialModule = manifest.Edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase);
        if (commercial != isCommercialModule) return false;

        if (manifest.Id.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (isCommercialModule &&
            manifest.Id.Equals(prefix + "-commercial", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
