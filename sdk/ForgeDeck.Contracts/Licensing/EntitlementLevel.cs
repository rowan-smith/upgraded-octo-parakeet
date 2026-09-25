namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Per-module entitlement tier. Application code must never branch on a global "is Enterprise"
/// flag — ask for the entitlement of a specific module (or a capability derived from it).
/// </summary>
public enum EntitlementLevel
{
    Community = 0,
    Team = 1,
    Enterprise = 2
}

public static class EntitlementLevelParser
{
    public static EntitlementLevel Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EntitlementLevel.Community;
        }

        if (value.Equals("enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return EntitlementLevel.Enterprise;
        }
        // Legacy "Commercial" maps to Team (historical open-core wording).
        if (value.Equals("team", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("commercial", StringComparison.OrdinalIgnoreCase))
        {
            return EntitlementLevel.Team;
        }

        return EntitlementLevel.Community;
    }

    public static string ToLicenceEdition(EntitlementLevel level) => level switch
    {
        EntitlementLevel.Enterprise => "Enterprise",
        EntitlementLevel.Team => "Team",
        _ => "Community"
    };
}
