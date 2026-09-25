using ForgeDeck.Contracts.Licensing;

namespace ForgeDeck.Core.Licensing;

/// <summary>
/// Commercial SKUs expand to per-module entitlement maps. There is no special "Team installation" —
/// only five module entitlements (plus optional platform features).
/// </summary>
public static class LicenceSkuCatalog
{
    public const string TeamBundle = "TEAM-BUNDLE";
    public const string EnterpriseBundle = "ENTERPRISE-BUNDLE";

    public static IReadOnlyDictionary<string, EntitlementLevel> Expand(string sku) =>
        sku.ToUpperInvariant() switch
        {
            TeamBundle => Bundle(EntitlementLevel.Team),
            EnterpriseBundle or "ENTERPRISE-PLATFORM" => Bundle(EntitlementLevel.Enterprise),
            "GIT-TEAM" => Single(PlatformModules.Git, EntitlementLevel.Team),
            "GIT-ENTERPRISE" => Single(PlatformModules.Git, EntitlementLevel.Enterprise),
            "CODE-TEAM" => Single(PlatformModules.Code, EntitlementLevel.Team),
            "CODE-ENTERPRISE" => Single(PlatformModules.Code, EntitlementLevel.Enterprise),
            "REVIEW-TEAM" => Single(PlatformModules.Review, EntitlementLevel.Team),
            "REVIEW-ENTERPRISE" => Single(PlatformModules.Review, EntitlementLevel.Enterprise),
            "BUILD-TEAM" => Single(PlatformModules.Build, EntitlementLevel.Team),
            "BUILD-ENTERPRISE" => Single(PlatformModules.Build, EntitlementLevel.Enterprise),
            "DEPLOY-TEAM" => Single(PlatformModules.Deploy, EntitlementLevel.Team),
            "DEPLOY-ENTERPRISE" => Single(PlatformModules.Deploy, EntitlementLevel.Enterprise),
            "BUILD-DEPLOY-TEAM" => new Dictionary<string, EntitlementLevel>(StringComparer.OrdinalIgnoreCase)
            {
                [PlatformModules.Git] = EntitlementLevel.Community,
                [PlatformModules.Code] = EntitlementLevel.Community,
                [PlatformModules.Review] = EntitlementLevel.Community,
                [PlatformModules.Build] = EntitlementLevel.Team,
                [PlatformModules.Deploy] = EntitlementLevel.Team
            },
            _ => throw new ArgumentException($"Unknown licence SKU '{sku}'.")
        };

    public static LicenceDocument CreateDocument(
        string sku,
        Guid organisationId,
        Guid? instanceId = null,
        string? licenceId = null,
        string? customer = null,
        int? maxUsers = null,
        DateTimeOffset? maintenanceUntil = null,
        int? maxMajor = null)
    {
        var map = Expand(sku);
        var document = new LicenceDocument
        {
            Version = 1,
            LicenceId = licenceId ?? $"lic_{organisationId:N}"[..12],
            Customer = customer,
            OrganisationId = organisationId,
            IssuedAt = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            Capacity = maxUsers is null ? null : new LicenceCapacityDocument { Users = maxUsers },
            Maintenance = maintenanceUntil is null ? null : new LicenceMaintenanceDocument { Until = maintenanceUntil },
            ProductVersion = maxMajor is null ? null : new LicenceProductVersionDocument { MaxMajor = maxMajor },
            Modules = map.ToDictionary(
                pair => pair.Key,
                pair => new LicenceModuleDocument { Edition = EntitlementLevelParser.ToLicenceEdition(pair.Value) },
                StringComparer.OrdinalIgnoreCase)
        };
        return document;
    }

    private static Dictionary<string, EntitlementLevel> Bundle(EntitlementLevel level) =>
        PlatformModules.All.ToDictionary(id => id, _ => level, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, EntitlementLevel> Single(string moduleId, EntitlementLevel level)
    {
        var map = PlatformModules.All.ToDictionary(id => id, _ => EntitlementLevel.Community, StringComparer.OrdinalIgnoreCase);
        map[moduleId] = level;
        return map;
    }
}
