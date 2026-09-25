using ForgeDeck.Contracts.Capabilities;

namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Maps module entitlement tiers to capability sets. Bundles and SKUs only pick tiers;
/// runtime always resolves capabilities from these tables (plus optional explicit overrides).
/// </summary>
public static class ModuleEntitlementTables
{
    public static IReadOnlySet<string> CapabilitiesFor(string moduleId, EntitlementLevel level)
    {
        var id = PlatformModules.Normalize(moduleId);
        return id switch
        {
            PlatformModules.Review => ReviewCaps(level),
            PlatformModules.Deploy => DeployCaps(level),
            PlatformModules.Build => BuildCaps(level),
            PlatformModules.Git => GitCaps(level),
            PlatformModules.Code => CodeCaps(level),
            _ => Empty
        };
    }

    public static EntitlementLevel MinimumLevelFor(string capability)
    {
        if (!KnownCapabilities.IsCommercial(capability))
        {
            return EntitlementLevel.Community;
        }

        if (KnownCapabilities.Review.Enterprise.Contains(capability) ||
            KnownCapabilities.Deploy.Enterprise.Contains(capability) ||
            KnownCapabilities.Build.Enterprise.Contains(capability) ||
            KnownCapabilities.Git.Enterprise.Contains(capability) ||
            KnownCapabilities.Code.Enterprise.Contains(capability))
        {
            return EntitlementLevel.Enterprise;
        }

        return EntitlementLevel.Team;
    }

    private static readonly HashSet<string> Empty = new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> ReviewCaps(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => Union(KnownCapabilities.Review.Team, KnownCapabilities.Review.Enterprise),
        EntitlementLevel.Team => KnownCapabilities.Review.Team,
        _ => Empty
    };

    private static IReadOnlySet<string> DeployCaps(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => Union(KnownCapabilities.Deploy.Team, KnownCapabilities.Deploy.Enterprise),
        EntitlementLevel.Team => KnownCapabilities.Deploy.Team,
        _ => Empty
    };

    private static IReadOnlySet<string> BuildCaps(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => Union(KnownCapabilities.Build.Team, KnownCapabilities.Build.Enterprise),
        EntitlementLevel.Team => KnownCapabilities.Build.Team,
        _ => Empty
    };

    private static IReadOnlySet<string> GitCaps(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => Union(KnownCapabilities.Git.Team, KnownCapabilities.Git.Enterprise),
        EntitlementLevel.Team => KnownCapabilities.Git.Team,
        _ => Empty
    };

    private static IReadOnlySet<string> CodeCaps(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => Union(KnownCapabilities.Code.Team, KnownCapabilities.Code.Enterprise),
        EntitlementLevel.Team => KnownCapabilities.Code.Team,
        _ => Empty
    };

    private static HashSet<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
        new(a.Concat(b), StringComparer.OrdinalIgnoreCase);
}
