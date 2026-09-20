using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Core.Context;

namespace Platform.Core.Api;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this WebApplication app) =>
        app.MapPost("/api/auth/login", (LoginRequest request, PlatformContextStore context) =>
            request.Email.Equals("maya@northstar.dev", StringComparison.OrdinalIgnoreCase) && request.Password == "demo"
                ? Results.Ok(new { token = "mvp-admin-token", user = context.User })
                : Results.Unauthorized());
}

public sealed record LoginRequest(string Email, string Password);
