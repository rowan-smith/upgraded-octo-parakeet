using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Identity;

public sealed class PermissionAuthorizer
{
    public bool Has(HttpContext context, string permission)
    {
        if (context.Items["user"] is not PlatformUser user)
        {
            return false;
        }

        foreach (var key in PermissionAliases.Expand(permission))
        {
            if (user.Permissions.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    public static IResult Forbidden() => Results.Json(new { error = "Forbidden", title = "Permission Error" }, statusCode: StatusCodes.Status403Forbidden);
}
