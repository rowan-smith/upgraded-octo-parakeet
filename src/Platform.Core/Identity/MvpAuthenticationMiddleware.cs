using Microsoft.AspNetCore.Http;
using Platform.Core.Context;

namespace Platform.Core.Identity;

public sealed class MvpAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PlatformContextStore platformContext)
    {
        if (!RequiresAuthentication(context.Request.Path)) { await next(context); return; }
        if (context.Request.Headers.Authorization.ToString() != "Bearer mvp-admin-token")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Sign in required" });
            return;
        }
        context.Items["user"] = platformContext.User;
        await next(context);
    }

    private static bool RequiresAuthentication(PathString path) =>
        path.StartsWithSegments("/api") && path != "/api/auth/login";
}
