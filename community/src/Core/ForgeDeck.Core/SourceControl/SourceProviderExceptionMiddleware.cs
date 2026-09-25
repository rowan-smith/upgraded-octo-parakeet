using System.Text.Json;
using ForgeDeck.Contracts.SourceControl;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.SourceControl;

public sealed class SourceProviderExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (SourceProviderException exception)
        {
            context.Response.StatusCode = exception.Kind switch
            {
                SourceProviderErrorKind.Authentication => StatusCodes.Status401Unauthorized,
                SourceProviderErrorKind.NotFound => StatusCodes.Status404NotFound,
                SourceProviderErrorKind.Conflict or SourceProviderErrorKind.RepositoryState => StatusCodes.Status409Conflict,
                SourceProviderErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                title = exception.Kind.ToString(),
                detail = exception.Message,
                retryAfterSeconds = exception.RetryAfterSeconds
            }));
        }
        catch (KeyNotFoundException exception)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = exception.Message });
        }
    }
}
