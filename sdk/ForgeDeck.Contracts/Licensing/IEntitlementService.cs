namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Resolves per-module entitlement levels. Prefer this (or capability checks) over global edition flags.
/// </summary>
public interface IEntitlementService
{
    EntitlementLevel GetEntitlement(Guid organisationId, string moduleId);

    bool HasEntitlement(Guid organisationId, string moduleId, EntitlementLevel required);

    void Require(Guid organisationId, string moduleId, EntitlementLevel required);

    SeatCapacityStatus GetSeatStatus(Guid organisationId);

    MaintenanceStatus GetMaintenanceStatus();
}

public sealed record SeatCapacityStatus(int? LicensedUsers, int ActiveUsers, bool OverCapacity, string? Warning);

public sealed record MaintenanceStatus(
    DateTimeOffset? MaintenanceUntil,
    int? MaxMajorVersion,
    bool MaintenanceExpired,
    bool VersionAllowed);
