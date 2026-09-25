using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Extensions;

/// <summary>Blocks normal module/connector APIs when the extension is not enabled in the registry.</summary>
public sealed class ExtensionGateMiddleware(RequestDelegate next)
{
    private static readonly (string Prefix, string RuntimeId)[] Gates =
    [
        ("/api/review", "review"),
        ("/api/pipelines", "pipelines"),
        ("/api/git", "git")
    ];

    public async Task InvokeAsync(HttpContext context, IExtensionRegistry registry)
    {
        var path = context.Request.Path.Value ?? "";
        foreach (var (prefix, runtimeId) in Gates)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (registry.IsRuntimeEnabled(runtimeId))
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
}

public static class ExtensionGateMiddlewareExtensions
{
    public static IApplicationBuilder UseExtensionGates(this IApplicationBuilder app) =>
        app.UseMiddleware<ExtensionGateMiddleware>();
}
