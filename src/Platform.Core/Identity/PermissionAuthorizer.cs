using Microsoft.AspNetCore.Http;

namespace Platform.Core.Identity;

public sealed class PermissionAuthorizer
{
    public bool Has(HttpContext context, string permission) =>
        context.Items["user"] is PlatformUser user && user.Permissions.Contains(permission);
}
