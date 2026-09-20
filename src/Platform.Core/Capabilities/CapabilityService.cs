using Platform.Contracts.Capabilities;
using Platform.Contracts.Licensing;
using Platform.Contracts.Modules;

namespace Platform.Core.Capabilities;

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
        .Where(module => module.Manifest.Edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase))
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
        if (!licences.TryGet(organisationId, out var entitlement)) return granted;

        var now = DateTimeOffset.UtcNow;
        foreach (var (moduleId, moduleLicence) in entitlement.Modules)
        {
            if (!ModuleAvailable(moduleId)) continue;
            if (moduleLicence.IsExpired(now)) continue;

            foreach (var capability in moduleLicence.Capabilities)
            {
                if (!KnownCapabilities.IsCommercial(capability)) continue;
                if (!_commercialDeclared.Contains(capability)) continue;
                granted.Add(capability);
            }
        }

        return granted;
    }

    public string EditionFor(Guid organisationId, string moduleId, string installedEdition)
    {
        if (!licences.TryGet(organisationId, out var entitlement)) return installedEdition;
        if (!TryResolveModuleLicence(entitlement, moduleId, out var moduleLicence)) return installedEdition;
        if (moduleLicence.IsExpired(DateTimeOffset.UtcNow)) return installedEdition;
        return string.IsNullOrWhiteSpace(moduleLicence.Edition) ? installedEdition : moduleLicence.Edition;
    }

    private bool ModuleAvailable(string moduleId) =>
        _modules.ContainsKey(moduleId) ||
        _modules.Keys.Any(id => id.StartsWith(moduleId + "-", StringComparison.OrdinalIgnoreCase));

    private static bool TryResolveModuleLicence(
        OrganisationLicenceEntitlement entitlement,
        string moduleId,
        out ModuleLicenceEntitlement moduleLicence)
    {
        if (entitlement.Modules.TryGetValue(moduleId, out moduleLicence!)) return true;
        const string suffix = "-commercial";
        if (moduleId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            var baseId = moduleId[..^suffix.Length];
            if (entitlement.Modules.TryGetValue(baseId, out moduleLicence!)) return true;
        }

        moduleLicence = null!;
        return false;
    }
}
