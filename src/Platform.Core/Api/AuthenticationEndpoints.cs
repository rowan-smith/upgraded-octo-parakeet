using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Platform.Core.Application;

namespace Platform.Core.Api;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth) =>
        {
            var result = auth.Login(request.Email, request.Password);
            return result is null ? Results.Unauthorized() : Results.Ok(new { result.Token, result.User, result.Profile });
        });
        app.MapPost("/api/auth/logout", (HttpContext context, AuthService auth) =>
        {
            if (context.Items["session-token"] is string token && token != "mvp-admin-token") auth.Logout(token);
            return Results.NoContent();
        });
    }
}

public sealed record LoginRequest(string Email, string Password);
