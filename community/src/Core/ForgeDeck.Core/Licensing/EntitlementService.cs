using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Licensing;

public sealed class EntitlementService(
    ILicenceEntitlementStore licences,
    ITenancyStore store) : IEntitlementService
{
    public const int ProductMajorVersion = 0;

    public EntitlementLevel GetEntitlement(Guid organisationId, string moduleId)
    {
        var id = PlatformModules.Normalize(moduleId);
        if (!licences.TryGet(organisationId, out var entitlement))
        {
            return EntitlementLevel.Community;
        }

        if (entitlement.ValidUntil is { } until && until <= DateTimeOffset.UtcNow)
        {
            return EntitlementLevel.Community;
        }

        if (!TryFindModule(entitlement, id, out var module))
        {
            return EntitlementLevel.Community;
        }

        if (module.IsExpired(DateTimeOffset.UtcNow))
        {
            return EntitlementLevel.Community;
        }

        return module.Level;
    }

    public bool HasEntitlement(Guid organisationId, string moduleId, EntitlementLevel required) =>
        GetEntitlement(organisationId, moduleId) >= required;

    public void Require(Guid organisationId, string moduleId, EntitlementLevel required)
    {
        if (HasEntitlement(organisationId, moduleId, required))
        {
            return;
        }

        throw new LicenceRequiredException($"{PlatformModules.Normalize(moduleId)}.{required}");
    }

    public SeatCapacityStatus GetSeatStatus(Guid organisationId)
    {
        var active = store.ListUsers().Count;
        int? licensed = null;
        if (licences.TryGet(organisationId, out var entitlement))
        {
            licensed = entitlement.MaxUsers;
        }

        if (licensed is null)
        {
            return new SeatCapacityStatus(null, active, false, null);
        }

        var over = active > licensed.Value;
        return new SeatCapacityStatus(
            licensed,
            active,
            over,
            over
                ? $"{active} active users. Your installation is {active - licensed.Value} users over its licensed capacity ({licensed}). Contact your administrator."
                : null);
    }

    public MaintenanceStatus GetMaintenanceStatus()
    {
        var organisationId = store.GetOrganisation()?.Id ?? KnownIds.OrganisationId;
        if (!licences.TryGet(organisationId, out var entitlement))
        {
            return new MaintenanceStatus(null, null, false, true);
        }

        var expired = entitlement.MaintenanceUntil is { } until && until <= DateTimeOffset.UtcNow;
        var maxMajor = entitlement.MaxMajorVersion;
        var versionAllowed = maxMajor is null || ProductMajorVersion <= maxMajor.Value;
        return new MaintenanceStatus(entitlement.MaintenanceUntil, maxMajor, expired, versionAllowed);
    }

    private static bool TryFindModule(
        OrganisationLicenceEntitlement entitlement,
        string moduleId,
        out ModuleLicenceEntitlement module)
    {
        if (entitlement.Modules.TryGetValue(moduleId, out module!))
        {
            return true;
        }

        foreach (var (key, value) in entitlement.Modules)
        {
            if (PlatformModules.Normalize(key).Equals(moduleId, StringComparison.OrdinalIgnoreCase))
            {
                module = value;
                return true;
            }
        }

        module = null!;
        return false;
    }
}
