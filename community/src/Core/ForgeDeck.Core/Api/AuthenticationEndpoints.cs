using ForgeDeck.Core.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Api;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth) =>
        {
            try
            {
                var result = auth.Login(request.Email, request.Password);
                return result is null ? Results.Unauthorized() : Results.Ok(new { result.Token, result.User, result.Profile });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
            }
        }).RequireRateLimiting("auth");
        app.MapPost("/api/auth/logout", (HttpContext context, AuthService auth) =>
        {
            if (context.Items["session-token"] is string token)
            {
                auth.Logout(token);
            }

            return Results.NoContent();
        });
    }
}

public sealed record LoginRequest(string Email, string Password);
