namespace Platform.Contracts.Licensing;

public interface ILicenceEntitlementStore
{
    bool TryGet(Guid organisationId, out OrganisationLicenceEntitlement entitlement);
}

public sealed record OrganisationLicenceEntitlement(
    Guid OrganisationId,
    DateTimeOffset IssuedAt,
    IReadOnlyDictionary<string, ModuleLicenceEntitlement> Modules);

public sealed record ModuleLicenceEntitlement(
    string Edition,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt is { } expires && expires <= utcNow;
}
