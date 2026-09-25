using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Identity;

public sealed class PermissionAuthorizer
{
    public bool Has(HttpContext context, string permission) =>
        context.Items["user"] is PlatformUser user && user.Permissions.Contains(permission);

    public static IResult Forbidden() => Results.Json(new { error = "Forbidden", title = "Permission Error" }, statusCode: StatusCodes.Status403Forbidden);
}
