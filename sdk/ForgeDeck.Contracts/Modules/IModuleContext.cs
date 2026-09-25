using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;

namespace ForgeDeck.Contracts.Modules;

/// <summary>
/// Stable façade modules should prefer over reaching into Core types.
/// First-party in-process modules and out-of-process hosts both surface this contract.
/// </summary>
public interface IModuleContext
{
    Guid OrganisationId { get; }
    ICapabilityService Capabilities { get; }
    IEntitlementService Entitlements { get; }
    IEventPublisher Events { get; }
    IProviderCatalogue Providers { get; }
}

/// <summary>Optional lifecycle hooks beyond RegisterServices/MapEndpoints.</summary>
public interface IModuleLifecycle
{
    Task StartAsync(IModuleContext context, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
