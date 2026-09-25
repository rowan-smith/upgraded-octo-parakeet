namespace ForgeDeck.Contracts.Licensing;

public interface ILicenceEntitlementStore
{
    bool TryGet(Guid organisationId, out OrganisationLicenceEntitlement entitlement);
}

public sealed record OrganisationLicenceEntitlement(
    Guid OrganisationId,
    DateTimeOffset IssuedAt,
    IReadOnlyDictionary<string, ModuleLicenceEntitlement> Modules,
    string? LicenceId = null,
    string? Customer = null,
    DateTimeOffset? ValidUntil = null,
    int? MaxUsers = null,
    DateTimeOffset? MaintenanceUntil = null,
    int? MaxMajorVersion = null,
    Guid? InstanceId = null,
    IReadOnlyDictionary<string, bool>? Features = null);

public sealed record ModuleLicenceEntitlement(
    string Edition,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset? ExpiresAt)
{
    public EntitlementLevel Level => EntitlementLevelParser.Parse(Edition);

    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt is { } expires && expires <= utcNow;
}
