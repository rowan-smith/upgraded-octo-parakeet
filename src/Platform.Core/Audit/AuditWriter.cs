using Microsoft.AspNetCore.Http;
using Platform.Contracts.Audit;
using Platform.Core.Context;
using Platform.Core.Identity;

namespace Platform.Core.Audit;

public sealed class AuditWriter(AuditStore auditStore, PlatformContextStore platformContext, IHttpContextAccessor httpContext) : IAuditWriter
{
    public void Write(string module, string action, string resource, object? metadata = null)
    {
        var actor = httpContext.HttpContext?.Items["user"] as PlatformUser ?? platformContext.User;
        auditStore.Append(new AuditEvent(Guid.NewGuid(), actor.Name, platformContext.Organisation.Name,
            platformContext.Project.Name, module, action, resource, DateTimeOffset.UtcNow,
            httpContext.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"), metadata));
    }
}
