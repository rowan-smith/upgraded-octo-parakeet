using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Core.Context;
using Microsoft.AspNetCore.Http;

namespace ForgeDeck.Core.Capabilities;

public sealed class CapabilityAuthorizer(ICapabilityService capabilities, PlatformContextStore context)
{
    public bool Has(string capability) => capabilities.Has(context.Organisation.Id, capability);

    public void Ensure(string capability)
    {
        if (!Has(capability))
        {
            throw new LicenceRequiredException(capability);
        }
    }

    public static IResult Forbidden(string capability) => Results.Json(
        new { error = $"Licence required for capability '{capability}'.", title = "Licence Error", capability },
        statusCode: StatusCodes.Status403Forbidden);
}
