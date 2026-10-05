using ForgeDeck.Core.Application;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;
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
                if (result is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new
                {
                    token = result.Token,
                    mustChangePassword = result.MustChangePassword,
                    user = PublicUser(result.User),
                    profile = result.Profile
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
            }
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/change-password", (ChangePasswordRequest request, HttpContext context, AuthService auth) =>
        {
            Guid userId;
            if (context.Items["user"] is PlatformUser platformUser)
            {
                userId = platformUser.Id;
            }
            else if (context.Items["account"] is UserAccount account)
            {
                userId = account.Id;
            }
            else
            {
                return Results.Unauthorized();
            }

            try
            {
                auth.ChangePassword(userId, request.CurrentPassword, request.NewPassword);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/auth/logout", (HttpContext context, AuthService auth) =>
        {
            if (context.Items["session-token"] is string token)
            {
                auth.Logout(token);
            }

            return Results.NoContent();
        });
    }

    private static object PublicUser(UserAccount user) => new
    {
        user.Id,
        user.Email,
        user.Username,
        user.Status,
        user.MustChangePassword,
        user.CreatedAt,
        user.UpdatedAt,
        user.LastLoginAt
    };
}

public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
