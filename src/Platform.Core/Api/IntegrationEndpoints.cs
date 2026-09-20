using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Platform.Contracts.Audit;
using Platform.Contracts.Integrations;
using Platform.Core.Identity;

namespace Platform.Core.Api;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/core/integrations/github", (IProviderCredentialStore credentials) => Results.Ok(new { provider = "github", configured = credentials.IsConfigured("github") }));
        endpoints.MapPut("/api/core/integrations/github", SetGitHubCredential);
        endpoints.MapDelete("/api/core/integrations/github", DeleteGitHubCredential);
    }

    private static IResult SetGitHubCredential(GitHubCredentialRequest request, HttpContext context, IProviderCredentialStore credentials, PermissionAuthorizer authorizer, IAuditWriter audit)
    {
        if (!authorizer.Has(context, "core.integration.manage")) return PermissionAuthorizer.Forbidden();
        if (string.IsNullOrWhiteSpace(request.Token)) return Results.BadRequest(new { error = "A GitHub personal access token is required." });
        credentials.Set("github", request.Token); audit.Write("core", "core.integration.credential.updated", "github");
        return Results.Ok(new { provider = "github", configured = true });
    }

    private static IResult DeleteGitHubCredential(HttpContext context, IProviderCredentialStore credentials, PermissionAuthorizer authorizer, IAuditWriter audit)
    {
        if (!authorizer.Has(context, "core.integration.manage")) return PermissionAuthorizer.Forbidden();
        credentials.Delete("github"); audit.Write("core", "core.integration.credential.deleted", "github");
        return Results.NoContent();
    }
}

public sealed record GitHubCredentialRequest(string Token);
