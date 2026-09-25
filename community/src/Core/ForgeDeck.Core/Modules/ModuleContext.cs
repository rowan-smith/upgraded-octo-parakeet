using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Context;

namespace ForgeDeck.Core.Modules;

public sealed class ModuleContext(
    PlatformContextStore platformContext,
    ICapabilityService capabilities,
    IEntitlementService entitlements,
    IEventPublisher events,
    IProviderCatalogue providers) : IModuleContext
{
    public Guid OrganisationId => platformContext.Organisation.Id;
    public ICapabilityService Capabilities => capabilities;
    public IEntitlementService Entitlements => entitlements;
    public IEventPublisher Events => events;
    public IProviderCatalogue Providers => providers;
}
