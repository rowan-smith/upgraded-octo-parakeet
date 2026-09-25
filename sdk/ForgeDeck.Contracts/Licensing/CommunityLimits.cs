namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Soft Community ceilings. Unlimited real work is allowed; sophistication hits the paid boundary.
/// Prefer these over metered transaction caps (especially for air-gapped installs).
/// </summary>
public static class CommunityLimits
{
    /// <summary>Community may configure a single approval requirement (e.g. "main requires 1 approval").</summary>
    public const int ReviewMaxApprovalRules = 1;

    /// <summary>Community may have only one pipeline run Queued/Running at a time.</summary>
    public const int BuildMaxConcurrentPipelines = 1;

    /// <summary>Community may define a single deployment environment.</summary>
    public const int DeployMaxEnvironments = 1;
}
