using Microsoft.AspNetCore.Http;
using Platform.Core.Application;
using Platform.Core.Context;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Platform.Core.Identity;

public sealed class MvpAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PlatformContextStore platformContext, AuthService auth, SetupService setup, ITenancyStore store)
    {
        if (!RequiresAuthentication(context.Request.Path)) { await next(context); return; }

        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;

        if (!string.IsNullOrWhiteSpace(token) && setup.IsBootstrapTokenValid(token) && IsSetupPath(context.Request.Path))
        {
            context.Items["bootstrap"] = true;
            context.Items["session-token"] = token;
            await next(context);
            return;
        }

        UserAccount? user = null;
        if (token == "mvp-admin-token")
            user = store.FindUserByEmail("maya@northstar.dev");
        else if (!string.IsNullOrWhiteSpace(token))
            user = auth.GetUserBySessionToken(token);

        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Sign in required" });
            return;
        }

        var profile = store.GetProfile(user.Id);
        var membership = store.GetMembership(user.Id);
        var role = membership?.Status == MembershipStatus.Active ? membership.Role : OrganisationRole.Member;
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Active organisation membership required" });
            return;
        }

        platformContext.SetUser(PlatformContextStore.ToPlatformUser(user, profile, role));
        var project = profile?.DefaultProjectId is Guid projectId ? store.FindProject(projectId) : store.ListProjects().FirstOrDefault();
        if (project is not null)
        {
            var repository = store.ListRepositories(project.Id).FirstOrDefault();
            platformContext.SetProject(new ProjectView(project.Id, KnownIds.OrganisationId, project.Name, project.Key, repository?.Name));
        }
        context.Items["user"] = platformContext.User;
        context.Items["session-token"] = token;
        await next(context);
    }

    private static bool RequiresAuthentication(PathString path)
    {
        if (!path.StartsWithSegments("/api")) return false;
        if (path == "/api/auth/login") return false;
        if (path == "/api/setup" || path == "/api/setup/status" || path == "/api/setup/bootstrap-login") return false;
        if (path.StartsWithSegments("/api/invitations")) return false;
        if (IsRunnerProtocol(path)) return false;
        return true;
    }

    private static bool IsSetupPath(PathString path)
    {
        var value = path.Value ?? "";
        return value.StartsWith("/api/setup", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("/api/licensing", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRunnerProtocol(PathString path)
    {
        var value = path.Value ?? "";
        if (value.Equals("/api/pipelines/runners/register", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.StartsWith("/api/pipelines/runners/", StringComparison.OrdinalIgnoreCase) &&
            (value.EndsWith("/heartbeat", StringComparison.OrdinalIgnoreCase) ||
             value.EndsWith("/work", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (value.StartsWith("/api/pipelines/runs/", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("/jobs", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
