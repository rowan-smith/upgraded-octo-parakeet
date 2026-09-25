using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;

namespace ForgeDeck.Core.Capabilities;

public sealed class CapabilityService(
    IEnumerable<IPlatformModule> modules,
    ILicenceEntitlementStore licences) : ICapabilityService
{
    private readonly Dictionary<string, IPlatformModule> _modules = modules.ToDictionary(
        module => module.Manifest.Id, StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _community = modules
        .SelectMany(module => module.Manifest.Capabilities)
        .Where(capability => !KnownCapabilities.IsCommercial(capability))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _commercialDeclared = modules
        .Where(module => IsProprietaryEdition(module.Manifest.Edition))
        .SelectMany(module => module.Manifest.Capabilities)
        .Where(KnownCapabilities.IsCommercial)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Current => _community;

    public bool Has(string capability) => _community.Contains(capability);

    public bool Has(Guid organisationId, string capability) =>
        ForOrganisation(organisationId).Contains(capability);

    public IReadOnlySet<string> ForOrganisation(Guid organisationId)
    {
        var granted = new HashSet<string>(_community, StringComparer.OrdinalIgnoreCase);
        if (!licences.TryGet(organisationId, out var entitlement))
        {
            return granted;
        }

        if (entitlement.ValidUntil is { } until && until <= DateTimeOffset.UtcNow)
        {
            return granted;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var (moduleId, moduleLicence) in entitlement.Modules)
        {
            if (!ModuleAvailable(moduleId))
            {
                continue;
            }

            if (moduleLicence.IsExpired(now))
            {
                continue;
            }

            foreach (var capability in moduleLicence.Capabilities)
            {
                if (!KnownCapabilities.IsCommercial(capability))
                {
                    continue;
                }

                if (!_commercialDeclared.Contains(capability))
                {
                    continue;
                }

                granted.Add(capability);
            }
        }

        return granted;
    }

    public string EditionFor(Guid organisationId, string moduleId, string installedEdition)
    {
        if (!licences.TryGet(organisationId, out var entitlement))
        {
            return installedEdition;
        }

        if (entitlement.ValidUntil is { } until && until <= DateTimeOffset.UtcNow)
        {
            return installedEdition;
        }

        if (!TryResolveModuleLicence(entitlement, moduleId, out var moduleLicence))
        {
            return installedEdition;
        }

        if (moduleLicence.IsExpired(DateTimeOffset.UtcNow))
        {
            return installedEdition;
        }

        return EntitlementLevelParser.ToLicenceEdition(moduleLicence.Level);
    }

    private static bool IsProprietaryEdition(string edition) =>
        edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase) ||
        edition.Equals("Team", StringComparison.OrdinalIgnoreCase) ||
        edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase);

    private bool ModuleAvailable(string moduleId)
    {
        var id = PlatformModules.Normalize(moduleId);
        return _modules.Keys.Any(key =>
        {
            var normalized = PlatformModules.Normalize(key);
            return normalized.Equals(id, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryResolveModuleLicence(
        OrganisationLicenceEntitlement entitlement,
        string moduleId,
        out ModuleLicenceEntitlement moduleLicence)
    {
        var id = PlatformModules.Normalize(moduleId);
        if (entitlement.Modules.TryGetValue(id, out moduleLicence!))
        {
            return true;
        }

        foreach (var (key, value) in entitlement.Modules)
        {
            if (!PlatformModules.Normalize(key).Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            moduleLicence = value;
            return true;
        }

        moduleLicence = null!;
        return false;
    }
}
