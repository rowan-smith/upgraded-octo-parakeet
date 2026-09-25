using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;

namespace ForgeDeck.Core.Licensing;

/// <summary>
/// Expands licence module tiers into capability grants. Explicit capability lists on a module
/// are treated as custom overrides; empty lists expand from <see cref="ModuleEntitlementTables"/>.
/// </summary>
public static class LicenceEntitlementExpander
{
    public static OrganisationLicenceEntitlement FromDocument(LicenceDocument document)
    {
        var modules = new Dictionary<string, ModuleLicenceEntitlement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (moduleId, module) in document.Modules)
        {
            var normalized = PlatformModules.Normalize(moduleId);
            var level = EntitlementLevelParser.Parse(module.Edition);
            var edition = EntitlementLevelParser.ToLicenceEdition(level);
            IReadOnlyList<string> caps;
            if (module.Capabilities.Count > 0)
            {
                caps = module.Capabilities
                    .Where(KnownCapabilities.IsCommercial)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else
            {
                caps = ModuleEntitlementTables.CapabilitiesFor(normalized, level).ToArray();
            }

            modules[normalized] = new ModuleLicenceEntitlement(edition, caps, module.ExpiresAt ?? document.ValidUntil);
        }

        return new OrganisationLicenceEntitlement(
            document.OrganisationId,
            document.IssuedAt,
            modules,
            document.LicenceId,
            document.Customer,
            document.ValidUntil,
            document.Capacity?.Users,
            document.Maintenance?.Until,
            document.ProductVersion?.MaxMajor,
            document.InstanceId,
            document.Features);
    }
}
