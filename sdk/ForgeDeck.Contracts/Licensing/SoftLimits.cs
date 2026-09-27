namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Soft ceilings by entitlement. Null means unlocked (Team+ capability present).
/// </summary>
public static class SoftLimits
{
    /// <summary>Concurrent pipeline runs. Community = 1; Team+ with Build.Concurrent = unlimited.</summary>
    public static int? MaxConcurrentPipelines(bool hasBuildConcurrent) =>
        hasBuildConcurrent ? null : CommunityLimits.BuildMaxConcurrentPipelines;

    /// <summary>Deployment environments. Community = 1; Team+ with Deploy.MultiEnvironment = unlimited.</summary>
    public static int? MaxEnvironments(bool hasMultiEnvironment) =>
        hasMultiEnvironment ? null : CommunityLimits.DeployMaxEnvironments;
}
