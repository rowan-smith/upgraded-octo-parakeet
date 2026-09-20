using Microsoft.AspNetCore.Http;
using Platform.Contracts.Capabilities;
using Platform.Core.Context;

namespace Platform.Core.Capabilities;

public sealed class CapabilityAuthorizer(ICapabilityService capabilities, PlatformContextStore context)
{
    public bool Has(string capability) => capabilities.Has(context.Organisation.Id, capability);

    public void Ensure(string capability)
    {
        if (!Has(capability)) throw new LicenceRequiredException(capability);
    }

    public static IResult Forbidden(string capability) => Results.Json(
        new { error = $"Licence required for capability '{capability}'.", title = "Licence Error", capability },
        statusCode: StatusCodes.Status403Forbidden);
}
