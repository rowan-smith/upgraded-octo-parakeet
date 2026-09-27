using ForgeDeck.Contracts.Extensions;
using ForgeDeck.Contracts.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Extensions;

/// <summary>
/// Blocks module APIs when the owning extension is not runtime-enabled.
/// Ownership comes from module manifests and the builtin catalogue — Core does not switch on module names.
/// </summary>
public sealed class ExtensionGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IExtensionRegistry registry,
        IEnumerable<IPlatformModule>? modules = null)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        foreach (var (prefix, runtimeId) in ResolveGates(modules))
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (registry.IsRuntimeEnabled(runtimeId) ||
                registry.IsRuntimeEnabled(LegacyRuntimeAlias.Normalize(runtimeId)))
            {
                break;
            }

            // Also accept legacy pipelines runtime rows during migration.
            if (runtimeId.Equals("build", StringComparison.OrdinalIgnoreCase) &&
                registry.IsRuntimeEnabled("pipelines"))
            {
                break;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Extension unavailable",
                error = "The required module is not enabled. Install and enable it from Organisation Settings → Modules."
            });
            return;
        }

        await next(context);
    }

    public static IReadOnlyList<(string Prefix, string RuntimeId)> ResolveGates(IEnumerable<IPlatformModule>? modules)
    {
        var gates = new List<(string Prefix, string RuntimeId)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string prefix, string runtimeId)
        {
            var key = $"{prefix}|{runtimeId}";
            if (!seen.Add(key))
            {
                return;
            }

            gates.Add((prefix, runtimeId));
        }

        if (modules is not null)
        {
            foreach (var module in modules)
            {
                var prefixes = module.Manifest.ApiRoutePrefixes;
                if (prefixes is { Count: > 0 })
                {
                    foreach (var prefix in prefixes)
                    {
                        if (!string.IsNullOrWhiteSpace(prefix))
                        {
                            Add(prefix.TrimEnd('/'), module.Manifest.Id);
                        }
                    }
                }
            }
        }

        // Catalogue fallbacks for virtual / not-yet-loaded modules (e.g. Code before assembly exists).
        foreach (var entry in BuiltinExtensionCatalogue.All.Where(e => e.Type == ExtensionType.Module && e.RuntimeId is not null))
        {
            foreach (var prefix in BuiltinExtensionCatalogue.ApiPrefixesFor(entry.ExtensionId))
            {
                Add(prefix, entry.RuntimeId!);
            }
        }

        return gates;
    }
}

public static class ExtensionGateMiddlewareExtensions
{
    public static IApplicationBuilder UseExtensionGates(this IApplicationBuilder app) =>
        app.UseMiddleware<ExtensionGateMiddleware>();
}

/// <summary>Maps legacy runtime ids during one upgrade window.</summary>
public static class LegacyRuntimeAlias
{
    public static string Normalize(string runtimeId) =>
        runtimeId.Equals("pipelines", StringComparison.OrdinalIgnoreCase) ? "build" : runtimeId;

    public static bool EqualsCanonical(string runtimeId, string canonical) =>
        string.Equals(Normalize(runtimeId), canonical, StringComparison.OrdinalIgnoreCase);
}
